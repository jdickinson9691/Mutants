using ChronTravelers.Core.Items;
using ChronTravelers.Core.Monsters;

namespace ChronTravelers.Core.Time;

/// <summary>
/// Turns the tier-free catalog entries (<see cref="SpeciesDefinition"/>,
/// <see cref="ItemArchetypeDefinition"/>) into concrete
/// <see cref="Monster"/>s and <see cref="Item"/>s scaled to a specific
/// year, plus the year-scaled Warden and its trophy. All numbers come
/// from <see cref="Monsters.MonsterScaling"/> / <see cref="Items.LootScaling"/>
/// at <see cref="TimeScale.TierForYear"/>; archetypes only shift a
/// species off that baseline by a fixed amount.
/// </summary>
public static class TimelineContentFactory
{
    // A regular monster's drop is weighted toward sell/convert fodder —
    // a *guaranteed* junk piece (every kill leaves something worth picking
    // up) plus a real chance at a second — with a lesser chance of gear and
    // a smaller one of a consumable. So a couple of kills reliably funds a
    // purchase, and now and then you get armed.
    private const double SellFodderDropChance = 1.0;
    private const double SecondSellFodderDropChance = 0.35;
    private const double GearDropChance = 0.35;
    private const double ConsumableDropChance = 0.20;

    /// <summary>The <c>powerMultiplier</c> of a Warden's guaranteed weapon trophy — deep in the Legendary band (see <see cref="Rarity.ForPower"/>).</summary>
    private const double WardenTrophyPower = 2.8;

    // How much tougher an apex (Monster.IsApex, seeded a few to a year by
    // YearPopulation) is than the same species at the same year — a serious
    // optional fight, not a boss. Speed is untouched so the player can
    // still disengage and walk away.
    private const double ApexHpMultiplier = 2.4;
    private const double ApexAttackMultiplier = 1.35;
    private const double ApexDefenseMultiplier = 1.4;
    private const double ApexXpMultiplier = 3.5;
    private const double ApexIonMultiplier = 2.0;

    private static readonly string[] TrophyNouns =
        ["Blade", "Sigil", "Crown", "Gauntlet", "Reliquary", "Warhorn", "Chronometer", "Standard"];

    /// <summary>
    /// A factory for one instance of <paramref name="species"/> as it
    /// appears in <paramref name="year"/>. <paramref name="lootPool"/> is
    /// the set of item archetypes whose themes the species can drop —
    /// resolved by the caller (<see cref="TimeWorld"/>); a deterministic
    /// subset is baked into the returned factory's loot table.
    /// </summary>
    public static Func<Monster> ForSpecies(
        long worldSeed,
        SpeciesDefinition species,
        int year,
        IReadOnlyList<ItemArchetypeDefinition> lootPool)
    {
        var tier = TimeScale.TierForYear(year);
        var (hp, attack, defense, speed) = StatsFor(species.Archetype, tier);
        var xp = (int)Math.Round(MonsterScaling.XpReward(tier));
        var ions = (int)Math.Round(MonsterScaling.BaseIons(tier));
        var displayTier = DisplayTier(year);
        var tags = species.Tags;
        var name = species.Name;

        var loot = BuildLootTable(worldSeed, year, species.Id, lootPool);

        return () => new Monster(name, displayTier, hp, attack, defense, speed, xp, loot, tags, maxIons: ions);
    }

    /// <summary>
    /// A factory for an <b>apex</b> version of <paramref name="species"/> in
    /// <paramref name="year"/> (see <see cref="Monster.IsApex"/>): the same
    /// species, much tougher, with a loot table that reliably yields real
    /// gear. <see cref="YearPopulation.Seed"/> drops a few of these into a
    /// year alongside the regular roster.
    /// </summary>
    public static Func<Monster> ApexForSpecies(
        long worldSeed,
        SpeciesDefinition species,
        int year,
        IReadOnlyList<ItemArchetypeDefinition> lootPool)
    {
        var tier = TimeScale.TierForYear(year);
        var (hp, attack, defense, speed) = StatsFor(species.Archetype, tier);

        var apexHp = Math.Max(1, (int)Math.Round(hp * ApexHpMultiplier));
        var apexAttack = Math.Max(1, (int)Math.Round(attack * ApexAttackMultiplier));
        var apexDefense = Math.Max(0, (int)Math.Round(defense * ApexDefenseMultiplier));
        var apexXp = (int)Math.Round(MonsterScaling.XpReward(tier) * ApexXpMultiplier);
        var apexIons = (int)Math.Round(MonsterScaling.BaseIons(tier) * ApexIonMultiplier);
        var displayTier = DisplayTier(year);
        var tags = species.Tags;
        var name = $"Frayed {species.Name}";

        var loot = BuildApexLootTable(worldSeed, year, species.Id, lootPool);

        return () => new Monster(name, displayTier, apexHp, apexAttack, apexDefense, speed, apexXp, loot, tags, maxIons: apexIons, isApex: true);
    }

    /// <summary>The Time Shard's damage is this much more than the year's best weapon.</summary>
    private const double TimeShardEdge = 1.25;

    /// <summary>
    /// The Time Shard for <paramref name="year"/> — a melee weapon whose
    /// AttackBonus is <see cref="TimeShardEdge"/>× the highest of any weapon
    /// (melee or ranged) available in that year, with a Credit
    /// (<see cref="Item.Value"/>) that scales with the year. Exactly one is
    /// placed on the floor per year (see <see cref="YearPopulation.Seed"/>);
    /// monsters and NPCs can't take it.
    /// </summary>
    public static Item TimeShard(int year, IReadOnlyList<ItemArchetypeDefinition> itemArchetypes)
    {
        var tier = TimeScale.TierForYear(year);

        var bestWeaponBonus = itemArchetypes
            .Where(a => a.Type is ItemType.Weapon or ItemType.Ranged)
            .Select(a => LootScaling.EquipBonusFor(tier, a.PowerMultiplier))
            .DefaultIfEmpty(LootScaling.EquipBonusFor(tier, 1.0))
            .Max();

        return new Item(
            "Time Shard",
            ItemType.Weapon,
            DisplayTier(year),
            Rarity.Legendary,
            Value: (int)Math.Round(LootScaling.ValueFor(tier, Rarity.Legendary) * 2.0),
            AttackBonus: (int)Math.Round(bestWeaponBonus * TimeShardEdge),
            IsTimeShard: true);
    }

    /// <summary>A single random item scaled to <paramref name="year"/>, rarity-weighted so most floor loot is humble — for the ~third of the grid that's seeded with loot on year load.</summary>
    public static Item RandomFloorItem(Random rng, IReadOnlyList<ItemArchetypeDefinition> itemArchetypes, int year)
    {
        var pick = WeightedPick(itemArchetypes, a => a.Rarity.DropWeight(), rng);
        return ForArchetype(pick, year);
    }

    /// <summary>
    /// The Warden standing watch over a Warden year (see
    /// <see cref="WardenSchedule"/>): a "bullet sponge" — ~3× a
    /// regular monster's HP at the year's tier, same attack/defense/speed,
    /// a generous XP payout, and a guaranteed year-scaled Legendary
    /// weapon trophy. It blocks nothing; it just guards good loot until
    /// beaten once.
    /// </summary>
    public static Monster Warden(long worldSeed, int year)
    {
        var tier = TimeScale.TierForYear(year);
        var rng = DeterministicRandom.For(worldSeed, year, "warden");
        var noun = TrophyNouns[rng.Next(TrophyNouns.Length)];

        var trophy = new Item(
            $"Warden of {year}'s {noun}",
            ItemType.Weapon,
            DisplayTier(year),
            RarityExtensions.ForPower(WardenTrophyPower),
            Value: (int)Math.Round(LootScaling.ValueFor(tier, Rarity.Legendary)),
            AttackBonus: (int)Math.Round(LootScaling.EquipBonusFor(tier, WardenTrophyPower)));

        return new Monster(
            $"The Warden of {year}",
            DisplayTier(year),
            maxHp: (int)Math.Round(MonsterScaling.BaseHp(tier) * 3),
            attackPower: (int)Math.Round(MonsterScaling.BaseAttackPower(tier)),
            defense: (int)Math.Round(MonsterScaling.BaseDefense(tier)),
            speed: (int)Math.Round(MonsterScaling.BaseSpeed(tier)),
            xpReward: (int)Math.Round(MonsterScaling.XpReward(tier) * 5),
            lootTable: [new LootTableEntry(trophy, dropChance: 1.0)],
            tags: [],
            maxIons: (int)Math.Round(MonsterScaling.BaseIons(tier) * 2));
    }

    /// <summary>A concrete item from <paramref name="archetype"/> as it would drop / be stocked in <paramref name="year"/>.</summary>
    public static Item ForArchetype(ItemArchetypeDefinition archetype, int year)
    {
        var tier = TimeScale.TierForYear(year);
        var displayTier = DisplayTier(year);

        if (archetype.IsRanged)
        {
            return Item.CreateRanged(
                archetype.Name,
                displayTier,
                archetype.Rarity,
                archetype.RangedKind,
                archetype.AmmoCapacity,
                archetype.RangedEffect,
                magnitude: archetype.EffectMagnitude > 0 ? archetype.EffectMagnitude : 1.0,
                restrictedClass: archetype.RestrictedClass,
                powerMultiplier: archetype.PowerMultiplier);
        }

        // Weapons/armour take their combat bonus from the archetype's
        // power multiplier (which also fixed its rarity, in the loader);
        // everything else is flat.
        var equipBonus = archetype.IsEquippable
            ? (int)Math.Round(LootScaling.EquipBonusFor(tier, archetype.PowerMultiplier))
            : 0;

        return new Item(
            archetype.Name,
            archetype.Type,
            displayTier,
            archetype.Rarity,
            Value: (int)Math.Round(LootScaling.ValueFor(tier, archetype.Rarity)),
            AttackBonus: archetype.Type == ItemType.Weapon ? equipBonus : 0,
            DefenseBonus: archetype.Type == ItemType.Armor ? equipBonus : 0,
            RestrictedClass: archetype.RestrictedClass,
            ConsumableEffect: archetype.Effect,
            EffectMagnitude: archetype.EffectMagnitude,
            EffectDurationTicks: archetype.EffectDurationTicks);
    }

    /// <summary>The whole-number tier shown on a Monster/Item for a year — clamped to ≥ 1 (year 2000 → 1).</summary>
    public static int DisplayTier(int year) => Math.Max(1, (int)Math.Round(TimeScale.TierForYear(year)));

    private static IReadOnlyList<LootTableEntry> BuildLootTable(
        long worldSeed,
        int year,
        string speciesId,
        IReadOnlyList<ItemArchetypeDefinition> lootPool)
    {
        if (lootPool.Count == 0)
        {
            return [];
        }

        var rng = DeterministicRandom.For(worldSeed, year, $"loot:{speciesId}");
        var entries = new List<LootTableEntry>();

        void AddFrom(IEnumerable<ItemArchetypeDefinition> candidates, double dropChance)
        {
            var pool = candidates.ToList();
            if (pool.Count == 0)
            {
                return;
            }

            // Rarity-weighted within the category: a Legendary weapon is
            // still a far rarer roll than a Common one.
            var pick = WeightedPick(pool, a => a.Rarity.DropWeight(), rng);
            entries.Add(new LootTableEntry(ForArchetype(pick, year), dropChance));
        }

        AddFrom(lootPool.Where(a => a.Type == ItemType.Junk), SellFodderDropChance);
        AddFrom(lootPool.Where(a => a.Type == ItemType.Junk), SecondSellFodderDropChance);
        AddFrom(lootPool.Where(a => a.IsEquippable), GearDropChance);
        AddFrom(lootPool.Where(a => a.Type == ItemType.Consumable), ConsumableDropChance);

        if (entries.Count == 0)
        {
            // Pool had none of the three categories (shouldn't happen —
            // TimeWorld.LootPoolFor backfills) — still give the kill a payout.
            AddFrom(lootPool, SellFodderDropChance);
        }

        return entries;
    }

    /// <summary>
    /// An apex monster's drop (see <see cref="ApexForSpecies"/>): a
    /// guaranteed piece of gear biased toward the <em>strong</em> end of
    /// the pool, plus guaranteed sell fodder and good odds on a second
    /// piece of gear and a consumable — the payoff for taking an optional
    /// hard fight.
    /// </summary>
    private static IReadOnlyList<LootTableEntry> BuildApexLootTable(
        long worldSeed,
        int year,
        string speciesId,
        IReadOnlyList<ItemArchetypeDefinition> lootPool)
    {
        if (lootPool.Count == 0)
        {
            return [];
        }

        var rng = DeterministicRandom.For(worldSeed, year, $"apexloot:{speciesId}");
        var entries = new List<LootTableEntry>();

        void AddGear(double dropChance)
        {
            var pool = lootPool.Where(a => a.IsEquippable).ToList();
            if (pool.Count == 0)
            {
                return;
            }

            // Bias hard toward high power — the whole point of the fight.
            var pick = WeightedPick(pool, a => Math.Pow(Math.Max(0.3, a.PowerMultiplier), 2), rng);
            entries.Add(new LootTableEntry(ForArchetype(pick, year), dropChance));
        }

        void AddFrom(IEnumerable<ItemArchetypeDefinition> candidates, double dropChance)
        {
            var pool = candidates.ToList();
            if (pool.Count == 0)
            {
                return;
            }

            var pick = WeightedPick(pool, a => a.Rarity.DropWeight(), rng);
            entries.Add(new LootTableEntry(ForArchetype(pick, year), dropChance));
        }

        AddFrom(lootPool.Where(a => a.Type == ItemType.Junk), 1.0);
        AddGear(1.0);
        AddGear(0.4);
        AddFrom(lootPool.Where(a => a.Type == ItemType.Consumable), 0.6);

        if (entries.Count == 0)
        {
            AddFrom(lootPool, 1.0);
        }

        return entries;
    }

    /// <summary>Returns one element of <paramref name="items"/>, chosen with probability proportional to <paramref name="weight"/>.</summary>
    private static T WeightedPick<T>(IReadOnlyList<T> items, Func<T, double> weight, Random rng)
    {
        var total = items.Sum(weight);
        var roll = rng.NextDouble() * total;

        foreach (var item in items)
        {
            roll -= weight(item);
            if (roll <= 0)
            {
                return item;
            }
        }

        return items[^1];
    }

    private static (int Hp, int Attack, int Defense, int Speed) StatsFor(MonsterArchetype archetype, double tier)
    {
        var hp = MonsterScaling.BaseHp(tier);
        var atk = MonsterScaling.BaseAttackPower(tier);
        var def = MonsterScaling.BaseDefense(tier);
        var spd = MonsterScaling.BaseSpeed(tier);

        (double Hp, double Attack, double Defense, double Speed) shifted = archetype switch
        {
            MonsterArchetype.Baseline => (hp, atk, def, spd),
            MonsterArchetype.Caster => (hp - 8, atk + 3, Math.Max(2, def - 1), spd + 4),
            MonsterArchetype.Bruiser => (hp + 12, atk - 1, def + 3, spd - 2),
            MonsterArchetype.Skirmisher => (hp + 4, atk + 1, def, spd + 3),
            _ => throw new ArgumentOutOfRangeException(nameof(archetype), archetype, null),
        };

        return (
            Math.Max(1, (int)Math.Round(shifted.Hp)),
            Math.Max(1, (int)Math.Round(shifted.Attack)),
            Math.Max(0, (int)Math.Round(shifted.Defense)),
            Math.Max(1, (int)Math.Round(shifted.Speed)));
    }
}
