using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Monsters;

namespace ChronoTravelers.Engine.Combat;

/// <summary>
/// Rolls a monster's loot table on defeat. docs/GDD.md §5: drops come from
/// a "weighted table per monster." Each entry is rolled independently
/// (not mutually exclusive) against its own <see cref="LootTableEntry.DropChance"/>
/// — but a kill <b>always</b> yields at least one item (see
/// <see cref="RollForKill"/>): if every entry misses, the most-likely one
/// is forced; if the table is empty, a tier-scaled scrap is synthesised.
/// </summary>
public static class LootDropRoller
{
    /// <summary>Rolls each entry independently. May return an empty list — callers that represent an actual kill should use <see cref="RollForKill"/>.</summary>
    public static IReadOnlyList<Item> Roll(IReadOnlyList<LootTableEntry> lootTable, IRandomSource random)
    {
        List<Item>? dropped = null;

        foreach (var entry in lootTable)
        {
            if (random.NextDouble() < entry.DropChance)
            {
                (dropped ??= []).Add(entry.Item);
            }
        }

        return dropped ?? [];
    }

    /// <summary>
    /// Loot for a defeated <paramref name="monster"/> — guaranteed non-empty.
    /// Rolls the table normally; if nothing dropped, forces the highest-
    /// chance entry; if the table has no entries at all, drops a single
    /// scrap scaled to the monster's tier.
    /// </summary>
    public static IReadOnlyList<Item> RollForKill(Monster monster, IRandomSource random)
    {
        var rolled = Roll(monster.LootTable, random);
        if (rolled.Count > 0)
        {
            return rolled;
        }

        if (monster.LootTable.Count > 0)
        {
            var best = monster.LootTable[0];
            foreach (var entry in monster.LootTable)
            {
                if (entry.DropChance > best.DropChance)
                {
                    best = entry;
                }
            }

            return [best.Item];
        }

        return [Item.Create("Scrap", ItemType.Junk, Math.Max(1, monster.Tier), Core.Items.Rarity.Common)];
    }
}
