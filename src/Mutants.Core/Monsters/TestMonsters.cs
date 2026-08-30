using Mutants.Core.Items;

namespace Mutants.Core.Monsters;

/// <summary>
/// A small hardcoded tier-1 monster roster for engine/console sandbox use,
/// mirroring <see cref="World.TestLevel"/>. NOT launch content — the real
/// monster roster is future Content Agent work per docs/CONTENT_PLAN.md.
/// </summary>
public static class TestMonsters
{
    public static Monster Scavenger() => Monster.Create("Scavenger", tier: 1,
    [
        new LootTableEntry(Item.Create("Scrap Metal", ItemType.Junk, 1, Rarity.Common), dropChance: 0.6),
        new LootTableEntry(Item.Create("Rusty Shiv", ItemType.Weapon, 1, Rarity.Common), dropChance: 0.25),
    ]);

    public static Monster JunkGolem() => Monster.Create("Junk Golem", tier: 1,
    [
        new LootTableEntry(Item.Create("Scrap Plating", ItemType.Armor, 1, Rarity.Common), dropChance: 0.35),
        new LootTableEntry(Item.Create("Warped Gear", ItemType.Junk, 1, Rarity.Uncommon), dropChance: 0.4),
    ]);

    public static Monster FeralDog() => Monster.Create("Feral Dog", tier: 1,
    [
        new LootTableEntry(Item.Create("Torn Hide", ItemType.Junk, 1, Rarity.Common), dropChance: 0.7),
    ]);

    public static IReadOnlyList<Func<Monster>> All { get; } = [Scavenger, JunkGolem, FeralDog];
}
