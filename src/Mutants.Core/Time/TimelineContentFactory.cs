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
    /// <summary>How many item archetypes a regular monster's loot table pulls from its theme pool.</summary>
    private const int LootEntriesPerMonster = 2;

    private static readonly double[] LootDropChances = [0.5, 0.25, 0.15];

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
            Rarity.Legendary,
            Value: (int)Math.Round(LootScaling.ValueFor(tier, Rarity.Legendary)),
            AttackBonus: (int)Math.Round(LootScaling.CombatBonusFor(tier, Rarity.Legendary)));

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

        return new Item(
            archetype.Name,
            archetype.Type,
            displayTier,
            archetype.Rarity,
            Value: (int)Math.Round(LootScaling.ValueFor(tier, archetype.Rarity)),
            AttackBonus: archetype.Type == ItemType.Weapon ? (int)Math.Round(LootScaling.CombatBonusFor(tier, archetype.Rarity)) : 0,
            DefenseBonus: archetype.Type == ItemType.Armor ? (int)Math.Round(LootScaling.CombatBonusFor(tier, archetype.Rarity)) : 0,
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
        var indices = Enumerable.Range(0, lootPool.Count).ToList();
        var entries = new List<LootTableEntry>();

        for (var i = 0; i < LootEntriesPerMonster && indices.Count > 0; i++)
        {
            var pick = rng.Next(indices.Count);
            var archetype = lootPool[indices[pick]];
            indices.RemoveAt(pick);
            entries.Add(new LootTableEntry(ForArchetype(archetype, year), LootDropChances[Math.Min(i, LootDropChances.Length - 1)]));
        }

        return entries;
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
