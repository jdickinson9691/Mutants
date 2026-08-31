using Mutants.Core.Items;
using Mutants.Core.Monsters;

namespace Mutants.Core.Time;

/// <summary>
/// Turns the tier-free catalog entries (<see cref="SpeciesDefinition"/>,
/// <see cref="ItemArchetypeDefinition"/>) into concrete
/// <see cref="Monster"/>s and <see cref="Item"/>s scaled to a specific
/// year, plus the year-scaled Gatekeeper and its trophy. All numbers come
/// from <see cref="Monsters.MonsterScaling"/> / <see cref="Items.LootScaling"/>
/// at <see cref="TimeScale.TierForYear"/>; archetypes only shift a
/// species off that baseline by a fixed amount.
/// </summary>
public static class TimelineContentFactory
{
    // A regular monster's drop is weighted toward sell/convert fodder —
    // a near-certain junk piece plus a real chance at a second — with a
    // lesser chance of gear and a smaller one of a consumable. So a couple
    // of kills reliably funds a purchase, and now and then you get armed.
    private const double SellFodderDropChance = 0.9;
    private const double SecondSellFodderDropChance = 0.35;
    private const double GearDropChance = 0.35;
    private const double ConsumableDropChance = 0.20;

    /// <summary>The <c>powerMultiplier</c> of a Gatekeeper's guaranteed weapon trophy — deep in the Legendary band (see <see cref="Rarity.ForPower"/>).</summary>
    private const double GatekeeperTrophyPower = 2.8;

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
    /// The Gatekeeper standing watch over a Gatekeeper year (see
    /// <see cref="GatekeeperSchedule"/>): a "bullet sponge" — ~3× a
    /// regular monster's HP at the year's tier, same attack/defense/speed,
    /// a generous XP payout, and a guaranteed year-scaled Legendary
    /// weapon trophy. It blocks nothing; it just guards good loot until
    /// beaten once.
    /// </summary>
    public static Monster Gatekeeper(long worldSeed, int year)
    {
        var tier = TimeScale.TierForYear(year);
        var rng = DeterministicRandom.For(worldSeed, year, "gatekeeper");
        var noun = TrophyNouns[rng.Next(TrophyNouns.Length)];

        var trophy = new Item(
            $"Warden of {year}'s {noun}",
            ItemType.Weapon,
            DisplayTier(year),
            RarityExtensions.ForPower(GatekeeperTrophyPower),
            Value: (int)Math.Round(LootScaling.ValueFor(tier, Rarity.Legendary)),
            AttackBonus: (int)Math.Round(LootScaling.EquipBonusFor(tier, GatekeeperTrophyPower)));

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
