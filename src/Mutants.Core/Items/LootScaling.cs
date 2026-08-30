namespace Mutants.Core.Items;

/// <summary>
/// Tier-to-value scaling per docs/GDD.md §5: "every item has a tier equal
/// to the time-travel level it was generated on... tier drives base stats,
/// sell price, and Ion-conversion value." The GDD confirms tier drives
/// value but does not specify the curve — this linear baseline (10 * tier)
/// is original tuning pending Design Agent sign-off.
/// </summary>
public static class LootScaling
{
    public static int TierBaseValue(int tier)
    {
        if (tier < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(tier), tier, "Tier must be at least 1.");
        }

        return 10 * tier;
    }

    /// <summary>Final item value = tier baseline, modulated by rarity — docs/GDD.md §5.</summary>
    public static int ValueFor(int tier, Rarity rarity) =>
        (int)Math.Round(TierBaseValue(tier) * rarity.ValueMultiplier());
}
