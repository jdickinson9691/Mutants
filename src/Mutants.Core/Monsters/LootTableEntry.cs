using Mutants.Core.Items;

namespace Mutants.Core.Monsters;

/// <summary>
/// One weighted entry in a monster's drop table — docs/GDD.md §5:
/// "monster drops on defeat (weighted table per monster, scaled to the
/// time-travel level it lives on)." Entries are rolled independently (not
/// mutually exclusive), so a kill can drop zero, one, or several items.
/// </summary>
public sealed record LootTableEntry
{
    public Item Item { get; }
    public double DropChance { get; }

    public LootTableEntry(Item item, double dropChance)
    {
        if (dropChance is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(dropChance), dropChance, "Drop chance must be in (0, 1].");
        }

        Item = item;
        DropChance = dropChance;
    }
}
