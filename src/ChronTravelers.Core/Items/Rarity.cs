namespace ChronTravelers.Core.Items;

/// <summary>
/// Rarity bands that "modulate stat rolls within a tier" — docs/GDD.md §5.
/// Explicitly original design; the GDD notes the source material never
/// specified a rarity system.
///
/// For equippable loot (weapons / armour / ranged) rarity is no longer
/// hand-authored: it's <em>derived from the item's power</em> via
/// <see cref="RarityExtensions.ForPower"/> — a bigger damage/defence
/// multiplier means a rarer band — and rarity in turn drives how often
/// the archetype is rolled onto a loot table (<see cref="RarityExtensions.DropWeight"/>),
/// so the hardest-hitting weapons really are rare finds. Consumables and
/// junk still carry an authored rarity.
/// </summary>
public enum Rarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary,
}

public static class RarityExtensions
{
    /// <summary>
    /// Multiplier applied to an item's tier-baseline value. Original
    /// tuning (not GDD-specified) — a smooth ramp so Legendary is
    /// meaningfully double a Common item of the same tier.
    /// </summary>
    public static double ValueMultiplier(this Rarity rarity) => rarity switch
    {
        Rarity.Common => 1.0,
        Rarity.Uncommon => 1.15,
        Rarity.Rare => 1.35,
        Rarity.Epic => 1.6,
        Rarity.Legendary => 2.0,
        _ => throw new ArgumentOutOfRangeException(nameof(rarity), rarity, null),
    };

    /// <summary>
    /// The rarity band an equippable's <c>powerMultiplier</c> falls into —
    /// see <see cref="LootScaling.EquipBonusFor"/>. A "Standard" 1.0×
    /// weapon is Uncommon; below ~0.75× is Common ("crude"), and a ~2.6×+
    /// monster of a weapon is Legendary ("relic").
    /// </summary>
    public static Rarity ForPower(double powerMultiplier) => powerMultiplier switch
    {
        < 0.75 => Rarity.Common,
        < 1.30 => Rarity.Uncommon,
        < 1.90 => Rarity.Rare,
        < 2.60 => Rarity.Epic,
        _ => Rarity.Legendary,
    };

    /// <summary>
    /// Relative likelihood an archetype of this rarity is picked when a
    /// monster's loot table is built (<c>TimelineContentFactory</c>): a
    /// Legendary weapon is rolled ~1/24 as often as a Common one, so it's
    /// a genuine rare/unique find rather than just a different label.
    /// </summary>
    public static double DropWeight(this Rarity rarity) => rarity switch
    {
        Rarity.Common => 6.0,
        Rarity.Uncommon => 3.5,
        Rarity.Rare => 1.6,
        Rarity.Epic => 0.6,
        Rarity.Legendary => 0.25,
        _ => throw new ArgumentOutOfRangeException(nameof(rarity), rarity, null),
    };
}
