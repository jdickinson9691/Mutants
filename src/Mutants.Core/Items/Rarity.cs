namespace Mutants.Core.Items;

/// <summary>
/// Rarity bands that "modulate stat rolls within a tier" — docs/GDD.md §5.
/// Explicitly original design; the GDD notes the source material never
/// specified a rarity system.
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
}
