using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Diagnostics;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Time;
using ChronoTravelers.Core.World;
using ChronoTravelers.Engine;
using ChronoTravelers.Engine.Content;
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

    public static RunReport Run(CharacterClass characterClass, long worldSeed, int maxTicks, string contentDirectory, IReadOnlyList<AbilityData> allAbilities)
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
            RunLoop(bot, world, simulation, random, classAbilities, maxTicks, report);
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

    private static void RunLoop(Traveler bot, TimeWorld world, WorldSimulation simulation, IRandomSource random, List<AbilityData> classAbilities, int maxTicks, RunReport report)
    {
        var maxHit = 0;
        var ticksSinceMonster = 0;

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
            if (monster is not null)
            {
                idle = false;
                ticksSinceMonster = 0;
                FightBot.Fight(bot, monster, classAbilities, random, report, population);
                if (monster.Health.IsDead)
                {
                    population.RemoveMonster(monster);
                }
            }
            else
            {
                ticksSinceMonster++;
                TryHealOrConsume(bot);
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

    private static void TryHealOrConsume(Traveler bot)
    {
        var hpFraction = bot.Health.Max > 0 ? bot.Health.Current / (double)bot.Health.Max : 1.0;
        if (hpFraction >= 0.9)
        {
            return;
        }

        if (hpFraction < 0.5)
        {
            var healItem = bot.Inventory.FirstOrDefault(i => i.IsUsable && i.ConsumableEffect == ConsumableEffectType.Heal);
            if (healItem is not null)
            {
                bot.Consume(healItem);
                return;
            }
        }

        if (hpFraction < 0.7 && bot.Tachyons.Current > 5)
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
