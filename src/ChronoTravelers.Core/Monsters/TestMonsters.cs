using ChronoTravelers.Core.Items;

namespace ChronoTravelers.Core.Monsters;

/// <summary>
/// A small hardcoded monster roster for engine/console sandbox use,
/// mirroring <see cref="World.TestLevel"/>. Each monster takes a tier
/// (default 1) so the same roster reuses across time-travel levels with
/// docs/GDD.md §5-correct tier scaling on both the monster and its loot.
/// NOT launch content — the real monster roster is future Content Agent
/// work per docs/CONTENT_PLAN.md.
/// </summary>
public static class TestMonsters
{
    public static Monster Scavenger(int tier = 1) => Monster.Create("Scavenger", tier,
    [
        new LootTableEntry(Item.Create("Scrap Metal", ItemType.Junk, tier, Rarity.Common), dropChance: 0.6),
        new LootTableEntry(Item.Create("Rusty Shiv", ItemType.Weapon, tier, Rarity.Common), dropChance: 0.25),
    ]);

    public static Monster JunkGolem(int tier = 1) => Monster.Create("Junk Golem", tier,
    [
        new LootTableEntry(Item.Create("Scrap Plating", ItemType.Armor, tier, Rarity.Common), dropChance: 0.35),
        new LootTableEntry(Item.Create("Warped Gear", ItemType.Junk, tier, Rarity.Uncommon), dropChance: 0.4),
    ]);

    public static Monster FeralDog(int tier = 1) => Monster.Create("Feral Dog", tier,
    [
        new LootTableEntry(Item.Create("Torn Hide", ItemType.Junk, tier, Rarity.Common), dropChance: 0.7),
    ]);

    /// <summary>
    /// A level's "warden" boss — docs/GDD.md §3.2: unlocking a level
    /// requires "defeating that level's warden monster once." A
    /// "bullet sponge" by design (triple HP, same attack/defense as a
    /// regular same-tier monster) rather than also hitting harder: a
    /// character who can already beat regular monsters at this tier is
    /// guaranteed to out-damage the warden too, just over more
    /// rounds — stacking a defense/attack multiplier on top of extra HP
    /// compounds fast and risks an unwinnable fight, which a one-time
    /// mandatory gate should never be. Original tuning pending Design
    /// Agent sign-off; guarantees a rare trophy drop.
    /// </summary>
    public static Monster Warden(int tier) => new(
        $"Warden of Level {tier}",
        tier,
        maxHp: MonsterScaling.BaseHp(tier) * 3,
        attackPower: MonsterScaling.BaseAttackPower(tier),
        defense: MonsterScaling.BaseDefense(tier),
        speed: MonsterScaling.BaseSpeed(tier),
        xpReward: MonsterScaling.XpReward(tier) * 5,
        lootTable: [new LootTableEntry(Item.Create("Warden's Trophy", ItemType.Weapon, tier, Rarity.Rare), dropChance: 1.0)],
        creditReward: MonsterScaling.CreditReward(tier) * 5);

    /// <summary>Tier-1 roster — the exact set this project has used since milestone 3, unchanged.</summary>
    public static IReadOnlyList<Func<Monster>> All { get; } = [() => Scavenger(), () => JunkGolem(), () => FeralDog()];

    /// <summary>The same named monsters, scaled to any tier — for deeper time-travel levels.</summary>
    public static IReadOnlyList<Func<Monster>> RosterFor(int tier) =>
        [() => Scavenger(tier), () => JunkGolem(tier), () => FeralDog(tier)];
}
