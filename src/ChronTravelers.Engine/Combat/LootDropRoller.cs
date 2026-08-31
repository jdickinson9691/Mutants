using ChronTravelers.Core.Items;
using ChronTravelers.Core.Monsters;

namespace ChronTravelers.Engine.Combat;

/// <summary>
/// Rolls a monster's loot table on defeat. docs/GDD.md §5: drops come from
/// a "weighted table per monster." Each entry is rolled independently
/// (not mutually exclusive) against its own <see cref="LootTableEntry.DropChance"/>.
/// </summary>
public static class LootDropRoller
{
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
}
