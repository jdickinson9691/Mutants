namespace ChronoTravelers.Core.Items;

/// <summary>
/// Picks a specific instance among several same-named items in a pack —
/// shared by the console (<c>Program.FindInventoryItem</c>) and the
/// multiplayer server (<c>Commands.FindItem</c>) so a destructive action
/// like <c>convert</c> resolves duplicates the same way on both fronts.
/// </summary>
public static class ItemSelection
{
    /// <summary>
    /// The weakest of <paramref name="items"/> — original tuning, added so
    /// a destructive action (<c>convert</c>) picks the worst copy among
    /// several sharing a name, never an arbitrary one that might be the
    /// player's best. Concretely: a player can carry more than one Time
    /// Shard (one per visited year — see
    /// <see cref="ChronoTravelers.Core.Time.TimelineContentFactory.TimeShard"/>),
    /// and before this existed <c>convert time shard</c> destroyed whichever
    /// one happened to be first in the pack, which could just as easily be
    /// the strongest. Ordered lowest-first by <see cref="Item.Tier"/> (how
    /// far downstream the copy is from — "time"/"level"), then
    /// <see cref="Item.AttackBonus"/>/<see cref="Item.DefenseBonus"/>
    /// ("damage"), then <see cref="Item.AmmoRemaining"/> (a more-depleted
    /// ranged copy is the weaker one), then <see cref="Item.Value"/> as a
    /// final tiebreak. Applies uniformly to every item type — for
    /// non-wieldable duplicates (junk, consumables) sharing a name and tier
    /// this ordering is normally a no-op since they're otherwise identical,
    /// but it's the wieldable items (weapons/armor/ranged, Time Shard
    /// included) where picking the weakest actually matters.
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="items"/> is empty.</exception>
    public static Item Weakest(IEnumerable<Item> items) =>
        items
            .OrderBy(i => i.Tier)
            .ThenBy(i => i.AttackBonus)
            .ThenBy(i => i.DefenseBonus)
            .ThenBy(i => i.AmmoRemaining)
            .ThenBy(i => i.Value)
            .First();
}
