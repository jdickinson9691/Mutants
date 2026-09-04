using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Diagnostics;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Monsters;
using ChronoTravelers.Core.Time;
using ChronoTravelers.Core.World;
using ChronoTravelers.Engine;
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
    public static RunReport Run(CharacterClass characterClass, long worldSeed, int maxTicks, string contentDirectory, IReadOnlyList<AbilityData> allAbilities, double aggression = 1.0)
    {
        var world = LoadWorld(contentDirectory, worldSeed);
        var random = new SystemRandomSource(new Random(unchecked((int)worldSeed)));
        var simulation = new WorldSimulation(world, new List<Traveler>(), random, abilities: allAbilities);

        var bot = new Traveler($"{characterClass}Bot", characterClass);
        GiveStarterKit(bot);
        bot.PlaceAt(world.GetYear(bot.CurrentYear).Map.Start);

        var classAbilities = allAbilities
            .Where(a => string.Equals(a.Class, characterClass.ToString(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        var report = new RunReport { CharacterName = bot.Name, WorldSeed = worldSeed };

        void OnPassiveActivation(CharacterClass cls, PassiveHook hook, double magnitude)
        {
            if (cls != characterClass)
            {
                return;
            }

            var usage = report.PassiveUsage.TryGetValue(hook, out var u) ? u : report.PassiveUsage[hook] = new PassiveUsage();
            usage.Activations++;
            usage.TotalMagnitude += magnitude;
        }

        var previousListener = PassiveActivationTracker.Listener;
        PassiveActivationTracker.Listener = OnPassiveActivation;
        try
        {
            RunLoop(bot, world, simulation, random, classAbilities, maxTicks, report, aggression);
        }
        finally
        {
            PassiveActivationTracker.Listener = previousListener;
        }

        report.FinalLevel = bot.Level;
        report.FinalYear = bot.CurrentYear;
        report.FurthestYearReached = bot.FurthestYearReached;
        report.FinalCredits = bot.Credits;
        report.FinalTachyons = bot.Tachyons.Current;

        foreach (var passive in PassiveTraits.Unlocked(characterClass, bot.Level))
        {
            if (!report.PassiveUsage.ContainsKey(passive.Hook))
            {
                report.UnlockedButUnobserved.Add(passive.Name);
            }
        }

        return report;
    }

    private static void RunLoop(Traveler bot, TimeWorld world, WorldSimulation simulation, IRandomSource random, List<AbilityData> classAbilities, int maxTicks, RunReport report, double aggression)
    {
        var maxHit = 0;
        var ticksSinceMonster = 0;
        Monster? shadowTarget = null;
        var shadowTicks = 0;

        for (var tick = 0; tick < maxTicks; tick++)
        {
            if (bot.Health.IsDead)
            {
                report.DiedDuringRun = true;
                break;
            }

            var year = world.GetYear(bot.CurrentYear);
            var population = year.Population;
            var idle = true;

            var hpBeforeAction = bot.Health.Current;
            var monster = population.MonstersAt(bot.Position).FirstOrDefault(m => !m.Health.IsDead);

            // Below MonsterController.StartRoomGraceMaxLevel a fresh
            // character has ~28-30 HP and no gear — the same window the
            // game itself protects from monster movement into safe rooms.
            // Letting the bot deliberately court an ambush on top of that
            // organic early hazard (rather than fight/flee immediately)
            // turned every class's early runs into a near-certain instawipe
            // (verified: 4/5 classes died within ~40 ticks on every single
            // run of a battery). Only shadow once past that window.
            if (monster is not null && !ReferenceEquals(monster, shadowTarget) && shadowTarget is null
                && bot.Level > MonsterController.StartRoomGraceMaxLevel && random.NextDouble() >= EngageChance)
            {
                shadowTarget = monster;
                shadowTicks = 0;
            }

            var hpFraction = bot.Health.Max > 0 ? bot.Health.Current / (double)bot.Health.Max : 1.0;
            if (monster is not null && ReferenceEquals(monster, shadowTarget) && shadowTicks < ShadowGiveUpTicks && hpFraction > ShadowAbortHpFraction)
            {
                // Deliberately leaving this specific monster alone — stays
                // put (doesn't even roll movement) so its aggro can climb
                // toward Hostile instead of the bot wandering off and
                // resetting the clock. See EngageChance's doc comment.
                shadowTicks++;
                ticksSinceMonster++;
            }
            else if (monster is not null)
            {
                idle = false;
                ticksSinceMonster = 0;
                shadowTarget = null;
                FightBot.Fight(bot, monster, classAbilities, random, report, population);
                if (monster.Health.IsDead)
                {
                    population.RemoveMonster(monster);
                }
            }
            else if (random.NextDouble() < IdleTurnChance)
            {
                // A deliberate no-op turn — idle stays true and nothing else
                // happens this tick. See IdleTurnChance's doc comment.
                ticksSinceMonster++;
            }
            else
            {
                ticksSinceMonster++;
                TryHealOrConsume(bot, aggression);
                TryPickUpAndWieldBetterGear(bot, population);
                TryShop(bot, year);

                if (bot.CurrentYear < TimeScale.MaxYear && ShouldTravel(bot, ticksSinceMonster, random))
                {
                    var target = Math.Min(TimeScale.MaxYear, bot.CurrentYear + TravelStepMin + (int)(random.NextDouble() * (TravelStepMax - TravelStepMin)));
                    if (TimeTravelResolver.Travel(bot, world, target, random).Success)
                    {
                        idle = false;
                        ticksSinceMonster = 0;
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

            var hpAfterAction = bot.Health.Current;
            RecordHit(hpBeforeAction - hpAfterAction, ref maxHit);

            simulation.Tick(bot, playerActedIdly: idle);

            var hpAfterTick = bot.Health.Current;
            var tickDamage = hpAfterAction - hpAfterTick;
            if (tickDamage > 0)
            {
                report.AmbushesObserved++;
            }

            RecordHit(tickDamage, ref maxHit);

            report.TicksRun = tick + 1;
            if (!bot.Health.IsDead)
            {
                report.TicksSurvived = tick + 1;
            }
        }

        report.MaxHitTaken = maxHit;
    }

    private static void RecordHit(int damage, ref int maxHit)
    {
        if (damage > maxHit)
        {
            maxHit = damage;
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

    private static void TryHealOrConsume(Traveler bot, double aggression)
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

    private static void TryShop(Traveler bot, YearContent year)
    {
        var slot = year.StoreSlots.FirstOrDefault(s => s.Location.Equals(bot.Position) && s.Store is not null);
        if (slot?.Store is not { } store)
        {
            return;
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

    private static void GiveStarterKit(Traveler bot)
    {
        // Matches ChronoTravelers.Console's fresh-character starter kit exactly
        // (Program.cs), so the harness's arrival-year experience isn't
        // artificially easier or harder than a real player's.
        var starterWeapon = new Item("Standard-Issue Baton", ItemType.Weapon, 1, Rarity.Common, Value: 5, AttackBonus: 10);
        bot.AddToInventory(starterWeapon);
        bot.Wield(starterWeapon);
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
