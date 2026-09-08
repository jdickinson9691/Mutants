using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Diagnostics;
using ChronoTravelers.Core.Economy;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Monsters;
using ChronoTravelers.Core.Time;
using ChronoTravelers.Core.World;
using ChronoTravelers.Engine;
using ChronoTravelers.Engine.Combat;
using ChronoTravelers.Engine.Content;
using ChronoTravelers.Engine.Npc;
using ChronoTravelers.Engine.Simulation;

namespace ChronoTravelers.PlaytestHarness;

/// <summary>
/// Plays one class through a bounded number of world ticks, spatially, as
/// the "player" argument to <see cref="WorldSimulation.Tick"/> — so the
/// real spatial monster sim (movement, aggro, ambush) applies exactly as
/// it does for a human player, unlike the off-grid instant-resolve grind
/// <c>NpcController.Act</c> uses for background NPCs. That matters here:
/// three passives (Thick Hide, Fleet-Footed/Redundant Systems, Trauma
/// Ward) only ever trigger through an ambush, which the off-grid NPC path
/// never generates.
/// </summary>
public static class PlaytestRunner
{
    private const int TravelStepMin = 150;
    private const int TravelStepMax = 300;
    private const int TicksBeforeConsideringTravel = 20;
    private const double IdleTravelChance = 0.05;

    /// <summary>
    /// Chance, on a no-monster tick, that the bot takes a genuine idle turn
    /// (like a human pausing on `look`/`status`/`wait`) instead of moving,
    /// healing, or shopping. Without this, <c>idle</c> was false on
    /// essentially every tick — fighting, moving, and traveling are all
    /// real actions — so <c>WorldSimulation.Tick</c>'s ambush check
    /// (<c>playerActedIdly &amp;&amp; lingered</c>) could never actually
    /// fire: zero ambushes across every battery run so far, which means
    /// Thick Hide, Fleet-Footed/Redundant Systems, and Trauma Ward were
    /// structurally unreachable regardless of aggression.
    /// </summary>
    private const double IdleTurnChance = 0.15;

    /// <summary>
    /// Chance, the first tick a fresh monster is found sharing the bot's
    /// room, that the bot deliberately leaves it alone instead of
    /// engaging — and then keeps leaving that <em>same</em> monster alone
    /// every tick after (see <c>shadowTarget</c>) rather than re-rolling.
    /// Even with genuine idle turns wired up, ambushes still never fired:
    /// this bot fought any co-located monster instantly, every time, so
    /// aggro (AggroModel.CoLocatedPerTick, needing ~10 consecutive
    /// co-located ticks to reach HostileThreshold from 0) never got the
    /// consecutive ticks near a monster it needs — a flat per-tick reroll
    /// at 80% engage would need a run of that same low roll ten times in a
    /// row (0.2^10), astronomically rare. Committing to one monster once
    /// and holding fixes that; see <see cref="ShadowGiveUpTicks"/> for the
    /// bail-out if it never escalates (a Calm monster that wanders off
    /// before locking on, or an apex whose aggro gain is scaled way down).
    /// </summary>
    private const double EngageChance = 0.8;

    /// <summary>Ticks to keep deliberately ignoring a shadowTarget before giving up and resuming normal engagement — well past the ~10 co-located ticks a regular monster needs to reach Hostile from 0 aggro.</summary>
    private const int ShadowGiveUpTicks = 20;

    /// <summary>
    /// HP fraction below which the bot abandons a shadowTarget early and
    /// fights back for real instead. First cut of this had no such
    /// safety valve: once a shadowed monster went Hostile it could land
    /// several ambush hits in a row (the bot does nothing but stand there
    /// while shadowing, no counterattack), and with no floor every single
    /// battery run died. This isn't meant to be survivable indefinitely —
    /// letting a monster reach Hostile is a real, played-as-intended risk
    /// — just not a guaranteed death sentence for every run.
    /// </summary>
    private const double ShadowAbortHpFraction = 0.6;

    /// <param name="aggression">
    /// Scales down the bot's healing thresholds (1.0 = default caution;
    /// 1.25 = 25% more aggressive, i.e. thresholds divided by 1.25). Low-HP
    /// passives (Soldier "Second Wind"/"Unbreakable", Doctor "Trauma
    /// Ward"'s ambush-negate roll only fires on an ambush at all, not HP —
    /// see the class doc comment) need the bot to actually stay hurt for a
    /// beat instead of topping off the instant it's off peak HP, which the
    /// default caution rarely allows. Doesn't change fight-selection or
    /// travel pacing — HP tolerance only.
    /// </param>
    /// <param name="verboseFatal">Dumps the monster's stats and the full combat log to stderr for whichever fight actually kills the bot — see FightBot.Fight.</param>
    public static RunReport Run(CharacterClass characterClass, long worldSeed, int maxTicks, string contentDirectory, IReadOnlyList<AbilityData> allAbilities, double aggression = 1.0, bool verboseFatal = false)
    {
        var world = LoadWorld(contentDirectory, worldSeed);
        var random = new SystemRandomSource(new Random(unchecked((int)worldSeed)));

        // A small NPC population so NPC-side traits (Hoarder, Scavenger,
        // Trader, ...) have anyone to run on at all — the harness ran with
        // zero NPCs before. Excludes the class under test entirely: NPCs
        // run through the same PassiveActivationTracker call sites as the
        // player, and OnPassiveActivation below only filters by class, not
        // by "is this actually my bot" — an NPC of the same class would
        // silently blend its own passive activations into the report.
        var npcClassWeights = Enum.GetValues<CharacterClass>()
            .Where(c => c != characterClass)
            .ToDictionary(c => c, _ => 1.0);
        var npcs = NpcPopulation.Spawn(NpcPopulation.LocalPopulationTarget, world, random, npcClassWeights).ToList();

        var simulation = new WorldSimulation(world, npcs, random, npcClassWeights: npcClassWeights, abilities: allAbilities);

        var state = new BotState(characterClass, worldSeed, allAbilities, aggression, verboseFatal);
        var bot = state.Bot;
        var report = state.Report;

        // Keyed by reference: a respawned NPC is a brand-new Traveler
        // instance (WorldSimulation.RespawnDeadNpcs replaces the slot, it
        // doesn't reset one in place), so this naturally starts a dead
        // NPC's replacement back at 0 kills — matching how its Trait/Level/
        // Credits already reflect only its current incarnation.
        var npcKillCounts = new Dictionary<Traveler, int>();
        simulation.OnNpcAct = (npc, result) => RecordNpcAct(report, npc, result, npcKillCounts);

        GiveStarterKit(bot);
        bot.PlaceAt(world.GetYear(bot.CurrentYear).Map.Start);

        void OnPassiveActivation(CharacterClass cls, PassiveHook hook, double magnitude)
        {
            if (cls != characterClass)
            {
                return;
            }

            RecordPassive(report, hook, magnitude);
        }

        var previousListener = PassiveActivationTracker.Listener;
        PassiveActivationTracker.Listener = OnPassiveActivation;
        try
        {
            for (var tick = 0; tick < maxTicks; tick++)
            {
                if (bot.Health.IsDead)
                {
                    report.DiedDuringRun = true;
                    break;
                }

                var idle = StepBot(state, world, random);

                state.HpBeforeTick = bot.Health.Current;
                simulation.Tick(bot, playerActedIdly: idle);

                var tickDamage = state.HpBeforeTick - bot.Health.Current;
                if (tickDamage > 0)
                {
                    report.AmbushesObserved++;
                    report.RecordHit(tickDamage);
                }

                report.TicksRun = tick + 1;
                if (!bot.Health.IsDead)
                {
                    report.TicksSurvived = tick + 1;
                }
            }
        }
        finally
        {
            PassiveActivationTracker.Listener = previousListener;
        }

        FinalizeReport(report, bot, characterClass, world, npcs, npcKillCounts, populateNpcOutcomes: true);
        return report;
    }

    /// <summary>
    /// Plays every class in <paramref name="classes"/> as its own bot in
    /// ONE shared world, stepping all living bots each tick and resolving
    /// them together through <see cref="WorldSimulation.TickMultiplayer"/>
    /// (the same path a real shared-world host uses), until the last bot
    /// dies or <paramref name="maxTicks"/> is hit. NPCs are drawn from all
    /// five classes (nothing is excluded, since every class is now a
    /// player) — so NPC-side passive activations of a class that's also
    /// being played are folded into that class's report; NPCs are few and
    /// mostly grinding/retreating, so the contribution is small, but the
    /// economy hooks (ConvertValue, StoreDiscount) carry the most of it.
    /// The returned <see cref="SimultaneousResult.World"/> holds the shared
    /// NPC store-activity and NPC-outcome data (one world, one dataset).
    /// </summary>
    public static SimultaneousResult RunSimultaneous(IReadOnlyList<CharacterClass> classes, long worldSeed, int maxTicks, string contentDirectory, IReadOnlyList<AbilityData> allAbilities, double aggression = 1.0, bool verboseFatal = false)
    {
        var world = LoadWorld(contentDirectory, worldSeed);
        var random = new SystemRandomSource(new Random(unchecked((int)worldSeed)));

        var npcClassWeights = Enum.GetValues<CharacterClass>().ToDictionary(c => c, _ => 1.0);
        var npcs = NpcPopulation.Spawn(NpcPopulation.LocalPopulationTarget, world, random, npcClassWeights).ToList();
        var simulation = new WorldSimulation(world, npcs, random, npcClassWeights: npcClassWeights, abilities: allAbilities);

        var worldReport = new RunReport { CharacterName = "SharedWorld", WorldSeed = worldSeed };
        var npcKillCounts = new Dictionary<Traveler, int>();
        simulation.OnNpcAct = (npc, result) => RecordNpcAct(worldReport, npc, result, npcKillCounts);

        // `classes` may list a class more than once (the 2-per-class
        // battery — see Program.cs's "battery" mode). Number the duplicates
        // so each bot's report has a distinct name ("SoldierBot#1" /
        // "SoldierBot#2"); a class that appears once keeps its bare name.
        var perClassTotal = classes.GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
        var perClassSeen = new Dictionary<CharacterClass, int>();
        var states = classes.Select(c =>
        {
            var n = perClassSeen[c] = perClassSeen.GetValueOrDefault(c) + 1;
            var suffix = perClassTotal[c] > 1 ? $"#{n}" : "";
            return new BotState(c, worldSeed, allAbilities, aggression, verboseFatal, suffix);
        }).ToList();

        foreach (var s in states)
        {
            GiveStarterKit(s.Bot);
            s.Bot.PlaceAt(world.GetYear(s.Bot.CurrentYear).Map.Start);
        }

        // PassiveActivationTracker.Listener only carries the class, not the
        // Traveler instance, so two bots of the same class can't be told
        // apart here — every same-class activation is pooled onto the first
        // instance's report (the other's PassiveUsage stays empty by
        // design). Callers that aggregate per class sum across both, so the
        // pooled total is still correct at the class level.
        var reportByClass = states
            .GroupBy(s => s.Bot.Class)
            .ToDictionary(g => g.Key, g => g.First().Report);

        void OnPassiveActivation(CharacterClass cls, PassiveHook hook, double magnitude)
        {
            if (reportByClass.TryGetValue(cls, out var r))
            {
                RecordPassive(r, hook, magnitude);
            }
        }

        var previousListener = PassiveActivationTracker.Listener;
        PassiveActivationTracker.Listener = OnPassiveActivation;
        var totalTicks = 0;
        try
        {
            for (var tick = 0; tick < maxTicks; tick++)
            {
                var living = states.Where(s => !s.Bot.Health.IsDead).ToList();
                if (living.Count == 0)
                {
                    break;
                }

                totalTicks = tick + 1;

                foreach (var s in living)
                {
                    s.TickState.ActedIdly = StepBot(s, world, random);
                    s.HpBeforeTick = s.Bot.Health.Current;
                }

                simulation.TickMultiplayer(living.Select(s => s.TickState).ToList());

                foreach (var s in living)
                {
                    var tickDamage = s.HpBeforeTick - s.Bot.Health.Current;
                    if (tickDamage > 0)
                    {
                        s.Report.AmbushesObserved++;
                        s.Report.RecordHit(tickDamage);
                    }

                    s.Report.TicksRun = tick + 1;
                    if (!s.Bot.Health.IsDead)
                    {
                        s.Report.TicksSurvived = tick + 1;
                    }
                    else if (!s.DeathRecorded)
                    {
                        s.Report.DiedDuringRun = true;
                        s.DeathRecorded = true;
                    }
                }
            }
        }
        finally
        {
            PassiveActivationTracker.Listener = previousListener;
        }

        foreach (var s in states)
        {
            FinalizeReport(s.Report, s.Bot, s.Bot.Class, world, npcs, npcKillCounts, populateNpcOutcomes: false);
        }

        // NPC outcomes / trait counts are a world-level fact — record once.
        var ownedStoreOwners = world.VisitedYears
            .SelectMany(y => world.GetYear(y).StoreSlots)
            .Select(s => s.Store?.Owner)
            .Where(owner => owner is not null)
            .ToHashSet();
        foreach (var npc in npcs)
        {
            worldReport.NpcTraitsObserved[npc.Trait] = worldReport.NpcTraitsObserved.GetValueOrDefault(npc.Trait) + 1;
            worldReport.NpcOutcomes.Add(new NpcOutcome(npc.Trait, npc.Level, npc.Credits, npc.Inventory.Count, npc.FurthestYearReached, ownedStoreOwners.Contains(npc), npcKillCounts.GetValueOrDefault(npc)));
        }

        return new SimultaneousResult
        {
            PerClass = states.Select(s => s.Report).ToList(),
            World = worldReport,
            TotalTicks = totalTicks,
        };
    }

    private static void RecordPassive(RunReport report, PassiveHook hook, double magnitude)
    {
        var usage = report.PassiveUsage.TryGetValue(hook, out var u) ? u : report.PassiveUsage[hook] = new PassiveUsage();
        usage.Activations++;
        usage.TotalMagnitude += magnitude;
    }

    /// <summary>The single-tick decision logic for one bot — everything the inline RunLoop body did EXCEPT the WorldSimulation tick call (single-player does <see cref="WorldSimulation.Tick"/>; the simultaneous runner batches every bot into one <see cref="WorldSimulation.TickMultiplayer"/>). Returns the playerActedIdly flag.</summary>
    internal static bool StepBot(BotState s, TimeWorld world, IRandomSource random)
    {
        var bot = s.Bot;
        var report = s.Report;

        var year = world.GetYear(bot.CurrentYear);
        var population = year.Population;
        var idle = true;

        var monster = population.MonstersAt(bot.Position).FirstOrDefault(m => !m.Health.IsDead);

        // Mid-chase: already locked a ranged target and no longer sharing a
        // room with anything — try for a clear shot this tick, or reposition
        // and try again next tick.
        if (s.RangedTarget is { Health.IsDead: false } && monster is null && bot.EquippedRanged is { IsDepleted: false } chaseWeapon)
        {
            idle = false;
            s.TicksSinceMonster = 0;

            var shotDirection = RangedTargeting.FindClearShotDirection(year.Map, bot.Position, chaseWeapon.Range, s.RangedTarget.Position);
            if (shotDirection is not null)
            {
                FireRangedWeapon(bot, s.RangedTarget, chaseWeapon, random, report, population);
                s.RangedChaseTicks = 0;
                if (s.RangedTarget.Health.IsDead || chaseWeapon.IsDepleted)
                {
                    if (s.RangedTarget.Health.IsDead)
                    {
                        population.RemoveMonster(s.RangedTarget);
                    }
                    else
                    {
                        s.RangedGaveUpOn.Add(s.RangedTarget);
                    }

                    bot.SetRangedTarget(null);
                    s.RangedTarget = null;
                }
            }
            else
            {
                s.RangedChaseTicks++;
                var direction = PickExit(year.Map, bot.Position, random);
                if (direction is { } d && year.Map.TryMove(bot.Position, d) is { Success: true, Destination: { } dest })
                {
                    bot.MoveTo(dest);
                }

                if (s.RangedChaseTicks > ShadowGiveUpTicks)
                {
                    s.RangedGaveUpOn.Add(s.RangedTarget);
                    bot.SetRangedTarget(null);
                    s.RangedTarget = null;
                }
            }

            return idle;
        }

        if (s.RangedTarget is not null)
        {
            bot.SetRangedTarget(null);
            s.RangedTarget = null;
        }

        // Below MonsterController.StartRoomGraceMaxLevel a fresh character
        // has ~28-30 HP and no gear — only deliberately court an ambush
        // (shadow a monster) once past that protected window.
        if (monster is not null && !ReferenceEquals(monster, s.ShadowTarget) && s.ShadowTarget is null
            && bot.Level > MonsterController.StartRoomGraceMaxLevel && random.NextDouble() >= EngageChance)
        {
            s.ShadowTarget = monster;
            s.ShadowTicks = 0;
        }

        var hpFraction = bot.Health.Max > 0 ? bot.Health.Current / (double)bot.Health.Max : 1.0;
        if (monster is not null && ReferenceEquals(monster, s.ShadowTarget) && s.ShadowTicks < ShadowGiveUpTicks && hpFraction > ShadowAbortHpFraction)
        {
            s.ShadowTicks++;
            s.TicksSinceMonster++;
        }
        else if (monster is not null)
        {
            idle = false;
            s.TicksSinceMonster = 0;
            s.ShadowTarget = null;

            if (bot.EquippedRanged is { IsDepleted: false } && !s.RangedGaveUpOn.Contains(monster))
            {
                bot.SetRangedTarget(monster);
                s.RangedTarget = monster;
                s.RangedChaseTicks = 0;
                var direction = PickExit(year.Map, bot.Position, random);
                if (direction is { } d && year.Map.TryMove(bot.Position, d) is { Success: true, Destination: { } dest })
                {
                    bot.MoveTo(dest);
                }
            }
            else
            {
                FightBot.Fight(bot, monster, s.ClassAbilities, random, report, population, s.VerboseFatal);
                if (monster.Health.IsDead)
                {
                    population.RemoveMonster(monster);
                }
            }
        }
        else if (random.NextDouble() < IdleTurnChance)
        {
            s.TicksSinceMonster++;
        }
        else
        {
            s.TicksSinceMonster++;
            NoteBrokenGear(bot, report);
            TryHealOrConsume(bot, s.Aggression, report);
            TryPickUpAndWieldBetterGear(bot, population);
            TryShop(bot, year, report);

            if (bot.CurrentYear < TimeScale.MaxYear && ShouldTravel(bot, s.TicksSinceMonster, random))
            {
                var target = Math.Min(TimeScale.MaxYear, bot.CurrentYear + TravelStepMin + (int)(random.NextDouble() * (TravelStepMax - TravelStepMin)));
                if (TimeTravelResolver.Travel(bot, world, target, random).Success)
                {
                    idle = false;
                    s.TicksSinceMonster = 0;
                }
            }

            if (idle)
            {
                var direction = PickExit(year.Map, bot.Position, random);
                if (direction is { } d && year.Map.TryMove(bot.Position, d) is { Success: true, Destination: { } dest })
                {
                    bot.MoveTo(dest);
                    idle = false;
                }
            }
        }

        return idle;
    }

    private static void FinalizeReport(RunReport report, Traveler bot, CharacterClass characterClass, TimeWorld world, IReadOnlyList<Traveler> npcs, Dictionary<Traveler, int> npcKillCounts, bool populateNpcOutcomes)
    {
        report.FinalLevel = bot.Level;
        report.FinalYear = bot.CurrentYear;
        report.FurthestYearReached = bot.FurthestYearReached;
        report.FinalCredits = bot.Credits;
        report.FinalTachyons = bot.Tachyons.Current;
        report.EquippedWeaponAtEnd = DescribeItem(bot.EquippedWeapon);
        report.EquippedArmorAtEnd = DescribeItem(bot.EquippedArmor);
        report.EquippedRangedAtEnd = DescribeItem(bot.EquippedRanged);

        report.FinalInventoryCount = bot.Inventory.Count;
        foreach (var item in bot.Inventory)
        {
            var described = DescribeItem(item);
            if (described is not null)
            {
                report.FinalInventory.Add(described);
            }
        }

        // The simultaneous path records NpcOutcomes / NpcTraitsObserved
        // once into its shared-world report instead of per class.
        if (populateNpcOutcomes)
        {
            var ownedStoreOwners = world.VisitedYears
                .SelectMany(y => world.GetYear(y).StoreSlots)
                .Select(s => s.Store?.Owner)
                .Where(owner => owner is not null)
                .ToHashSet();

            foreach (var npc in npcs)
            {
                report.NpcTraitsObserved[npc.Trait] = report.NpcTraitsObserved.GetValueOrDefault(npc.Trait) + 1;
                report.NpcOutcomes.Add(new NpcOutcome(npc.Trait, npc.Level, npc.Credits, npc.Inventory.Count, npc.FurthestYearReached, ownedStoreOwners.Contains(npc), npcKillCounts.GetValueOrDefault(npc)));
            }
        }

        foreach (var passive in PassiveTraits.Unlocked(characterClass, bot.Level))
        {
            if (!report.PassiveUsage.ContainsKey(passive.Hook))
            {
                report.UnlockedButUnobserved.Add(passive.Name);
            }
        }
    }

    /// <summary>Folds one NPC's per-tick action into <paramref name="report"/> — kill count plus the NPC store-commerce split (see RunReport's NPC-store fields).</summary>
    private static void RecordNpcAct(RunReport report, Traveler npc, NpcTickResult result, Dictionary<Traveler, int> npcKillCounts)
    {
        if (result.Fight is { TravelerWon: true })
        {
            npcKillCounts[npc] = npcKillCounts.GetValueOrDefault(npc) + 1;
        }

        report.NpcActionCounts[result.Goal] = report.NpcActionCounts.GetValueOrDefault(result.Goal) + 1;

        // An NPC-owned (or player-owned) store is always named "<name>'s
        // Store" (StoreSlot.Purchase); the year's always-open government
        // store is "<era> Depot" (TimeWorld.Build). So a Trade detail
        // mentioning "'s Store" is an NPC buying from / selling to another
        // traveller's shopfront — exactly the NPC-to-NPC store commerce
        // we're checking for.
        if (result.Goal == NpcGoal.Trade && result.Detail is { } tradeDetail)
        {
            if (tradeDetail.Contains("'s Store", StringComparison.Ordinal))
            {
                report.NpcTradesAtPlayerOrNpcStore++;
            }
            else
            {
                report.NpcTradesAtGovernmentStore++;
            }

            if (report.NpcStoreActivitySamples.Count < 30)
            {
                report.NpcStoreActivitySamples.Add($"{npc.Name}: {tradeDetail}");
            }
        }
        else if (result.Goal == NpcGoal.OwnStore && result.Detail is { } ownDetail)
        {
            if (ownDetail.StartsWith("bought ", StringComparison.Ordinal))
            {
                report.NpcStoreSlotPurchases++;
            }
            else
            {
                report.NpcOwnStoreTendActions++;
            }

            if (report.NpcStoreActivitySamples.Count < 30)
            {
                report.NpcStoreActivitySamples.Add($"{npc.Name}: {ownDetail}");
            }
        }
    }

    /// <summary>
    /// Blind "travel forward whenever idle" (the harness's original policy)
    /// pushed every run into tiers the bot's level couldn't survive —
    /// smoke-testing this harness found 100% eventual death, including
    /// several runs dying deep with 50-100+ hits taken, well above what a
    /// level-appropriate fight should cost. A careful human doesn't
    /// out-travel their own level; this bot shouldn't either: only advance
    /// once roughly leveled for the CURRENT tier's band (10x tier, per
    /// MonsterScaling's doc comment / KillXp's falloff), so time is spent
    /// grinding a survivable tier instead of wandering into one that isn't.
    /// </summary>
    private static bool ShouldTravel(Traveler bot, int ticksSinceMonster, IRandomSource random)
    {
        if (ticksSinceMonster <= 5)
        {
            return false;
        }

        var bandCap = 10 * TimeScale.TierForYear(bot.CurrentYear);
        if (bot.Level < bandCap - 3)
        {
            return false;
        }

        return ticksSinceMonster > TicksBeforeConsideringTravel || random.NextDouble() < IdleTravelChance;
    }

    /// <summary>
    /// One shot at an already-lined-up target — the harness's counterpart
    /// to ChronoTravelers.Console's HandleShoot (command parsing/messages
    /// aside; the caller in RunLoop already found the firing direction via
    /// RangedTargeting). Awards XP/Credits and grounds loot on a kill,
    /// same as a melee win via FightBot.Fight.
    /// </summary>
    private static void FireRangedWeapon(Traveler bot, Monster target, Item weapon, IRandomSource random, RunReport report, YearPopulation population)
    {
        report.RangedShotsFired++;
        var result = RangedResolver.Fire(bot, target, weapon, random);
        if (result.Damage > 0)
        {
            report.RangedShotsHit++;
        }

        if (!result.Killed)
        {
            return;
        }

        report.RangedKills++;
        report.Kills++;
        var xpAwarded = MonsterScaling.KillXp(target.XpReward, target.Tier, bot.Level);
        bot.GainXp(xpAwarded);
        report.TotalXp += xpAwarded;
        var creditsAwarded = MonsterScaling.KillCredits(target.CreditReward, target.Tier, bot.Level);
        bot.AddCredits(creditsAwarded);

        foreach (var drop in LootDropRoller.RollForKill(target, random).Concat(target.Inventory))
        {
            population.AddGroundLoot(target.Position, drop);
        }
    }

    private static Direction? PickExit(LevelMap map, Coordinate at, IRandomSource random)
    {
        var room = map.TryGetRoom(at);
        if (room is null)
        {
            return null;
        }

        var exits = Enum.GetValues<Direction>().Where(room.HasExit).ToList();
        return exits.Count == 0 ? null : exits[(int)(random.NextDouble() * exits.Count)];
    }

    private static void TryHealOrConsume(Traveler bot, double aggression, RunReport report)
    {
        var hpFraction = bot.Health.Max > 0 ? bot.Health.Current / (double)bot.Health.Max : 1.0;
        if (hpFraction >= 0.9 / aggression)
        {
            return;
        }

        if (hpFraction < 0.5 / aggression)
        {
            var healItem = bot.Inventory.FirstOrDefault(i => i.IsUsable && i.ConsumableEffect == ConsumableEffectType.Heal);
            if (healItem is not null)
            {
                bot.Consume(healItem);
                report.RecordConsumableUse(ConsumableEffectType.Heal, inCombat: false);
                return;
            }
        }

        if (hpFraction < 0.7 / aggression && bot.Tachyons.Current > 5)
        {
            bot.Heal();
        }
    }

    private static void TryPickUpAndWieldBetterGear(Traveler bot, YearPopulation population)
    {
        while (bot.Inventory.Count < Traveler.MaxInventorySize)
        {
            var picked = population.TakeGroundLoot(bot.Position, _ => true);
            if (picked is null)
            {
                break;
            }

            if (!bot.AddToInventory(picked))
            {
                population.AddGroundLoot(bot.Position, picked);
                break;
            }
        }

        var weaponUpgrade = bot.Inventory
            .Where(i => i.Type == ItemType.Weapon && (bot.EquippedWeapon is null || i.AttackBonus > bot.EquippedWeapon.AttackBonus))
            .OrderByDescending(i => i.AttackBonus)
            .FirstOrDefault();
        if (weaponUpgrade is not null)
        {
            bot.Wield(weaponUpgrade);
        }

        var armorUpgrade = bot.Inventory
            .Where(i => i.Type == ItemType.Armor && (bot.EquippedArmor is null || i.DefenseBonus > bot.EquippedArmor.DefenseBonus))
            .OrderByDescending(i => i.DefenseBonus)
            .FirstOrDefault();
        if (armorUpgrade is not null)
        {
            bot.Wield(armorUpgrade);
        }

        // Without this, a looted ranged weapon just rides in the pack
        // forever — RunLoop's ranged-kiting branch only ever fires once
        // something's actually equipped in the slot.
        var rangedUpgrade = bot.Inventory
            .Where(i => i.Type == ItemType.Ranged && !i.IsDepleted && (bot.EquippedRanged is null || i.AttackBonus > bot.EquippedRanged.AttackBonus))
            .OrderByDescending(i => i.AttackBonus)
            .FirstOrDefault();
        if (rangedUpgrade is not null)
        {
            bot.Wield(rangedUpgrade);
        }

        if (bot.Inventory.Count < Traveler.MaxInventorySize - 2)
        {
            return;
        }

        // Housekeeping so the pack doesn't jam: junk always converts, then
        // the cheapest unequipped gear/ranged/spare consumables convert too
        // until there's room again.
        foreach (var junk in bot.Inventory.Where(i => i.Type == ItemType.Junk).ToList())
        {
            bot.Convert(junk);
        }

        var spares = bot.Inventory
            .Where(i => i != bot.EquippedWeapon && i != bot.EquippedArmor && i != bot.EquippedRanged)
            .OrderBy(i => i.Value)
            .ToList();
        foreach (var spare in spares)
        {
            if (bot.Inventory.Count < Traveler.MaxInventorySize - 2)
            {
                break;
            }

            bot.Convert(spare);
        }
    }

    private static void TryShop(Traveler bot, YearContent year, RunReport report)
    {
        var slot = year.StoreSlots.FirstOrDefault(s => s.Location.Equals(bot.Position) && s.Store is not null);
        if (slot?.Store is not { } store)
        {
            return;
        }

        // Repair whatever the bot is fighting with FIRST — docs/GDD.md §6.3's
        // durability/repair loop. Worn Weapon/Armor loses combat contribution
        // (Item.DurabilityEffectiveness, no floor) until repaired, and a real
        // player would top it up on a store visit before anything else. Only
        // the equipped pieces matter; a spare in the pack is dead weight
        // whether it's worn or not. Starter gear and ranged weapons never
        // have durability (Item.HasDurability), so those are skipped.
        foreach (var gear in new[] { bot.EquippedWeapon, bot.EquippedArmor })
        {
            if (gear is not { HasDurability: true } || gear.Durability >= gear.MaxDurability)
            {
                continue;
            }

            var cost = EconomyPricing.RepairCost(gear);
            if (cost <= 0 || bot.Credits < cost)
            {
                continue;
            }

            var paid = store.Repair(bot, gear);
            if (paid is { } spent)
            {
                report.RepairsPerformed++;
                report.CreditsSpentOnRepair += spent;
            }
        }

        foreach (var item in bot.Inventory
                     .Where(i => i != bot.EquippedWeapon && i != bot.EquippedArmor && i != bot.EquippedRanged
                                 && i.Type is ItemType.Weapon or ItemType.Armor or ItemType.Ranged or ItemType.Junk)
                     .Take(2)
                     .ToList())
        {
            store.BuyFromTraveler(bot, item);
        }

        var weaponListing = store.Listings
            .Where(l => l.Item.Type == ItemType.Weapon && l.AskingPrice <= bot.Credits
                        && (bot.EquippedWeapon is null || l.Item.AttackBonus > bot.EquippedWeapon.AttackBonus))
            .OrderByDescending(l => l.Item.AttackBonus)
            .FirstOrDefault();
        if (weaponListing is not null && store.SellToTraveler(bot, weaponListing))
        {
            bot.Wield(weaponListing.Item);
        }
    }

    /// <summary>Flags <see cref="RunReport.EquippedGearBrokeAtLeastOnce"/> if the bot is currently fighting with a fully-worn Weapon/Armor — i.e. the repair cadence (see <see cref="TryShop"/>) fell behind wear. Cheap; call each tick.</summary>
    private static void NoteBrokenGear(Traveler bot, RunReport report)
    {
        if (report.EquippedGearBrokeAtLeastOnce)
        {
            return;
        }

        if (bot.EquippedWeapon is { IsBroken: true } || bot.EquippedArmor is { IsBroken: true })
        {
            report.EquippedGearBrokeAtLeastOnce = true;
        }
    }

    /// <summary>Human-readable equipped-item summary for <see cref="RunReport"/> — null for an empty slot.</summary>
    private static string? DescribeItem(Item? item)
    {
        if (item is null)
        {
            return null;
        }

        var bonus = item.Type switch
        {
            ItemType.Weapon or ItemType.Ranged => $"+{item.AttackBonus} atk",
            ItemType.Armor => $"+{item.DefenseBonus} def",
            _ => "",
        };
        var ammo = item.Type == ItemType.Ranged ? $", {item.AmmoRemaining}/{item.AmmoCapacity} ammo" : "";
        var shard = item.IsTimeShard ? " [Time Shard]" : "";

        return $"{item.Name} (tier {item.Tier}, {item.Rarity}, {bonus}{ammo}){shard}";
    }

    private static void GiveStarterKit(Traveler bot)
    {
        // Matches ChronoTravelers.Game.CharacterFactory.NewTraveler exactly
        // (which both the console and the server build a fresh character
        // through), so the harness's arrival-year experience isn't
        // artificially easier or harder than a real player's. Both starter
        // pieces are bare `new Item(...)` — MaxDurability 0, so they never
        // wear out or need repair (Item.HasDurability).
        var starterWeapon = new Item("Standard-Issue Baton", ItemType.Weapon, 1, Rarity.Common, Value: 5, AttackBonus: 10);
        bot.AddToInventory(starterWeapon);
        bot.Wield(starterWeapon);
        var starterArmor = new Item("Standard-Issue Vest", ItemType.Armor, 1, Rarity.Common, Value: 5, DefenseBonus: 8);
        bot.AddToInventory(starterArmor);
        bot.Wield(starterArmor);
        for (var i = 0; i < 3; i++)
        {
            bot.AddToInventory(Item.Create("Field Ration", ItemType.Consumable, 1, Rarity.Common,
                consumableEffect: ConsumableEffectType.Heal, effectMagnitude: 12));
        }
    }

    private static TimeWorld LoadWorld(string contentDirectory, long worldSeed)
    {
        try
        {
            return ContentLoader.LoadTimeWorld(contentDirectory, worldSeed);
        }
        catch (ContentException)
        {
            return TestTimeWorld.Build(worldSeed);
        }
    }
}
