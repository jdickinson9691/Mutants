namespace Mutants.Core.Items;

/// <summary>
/// Tier-to-value scaling per docs/GDD.md §5: "every item has a tier equal
/// to the point in the timeline it was generated on... tier drives base
/// stats, sell price, and Ion-conversion value." The GDD confirms tier
/// drives value but does not specify the curve — this linear baseline
/// (<c>12 * tier + 10</c>) is original tuning pending Design Agent
/// sign-off. The flat <c>+10</c> is a front-loaded bump from the earlier
/// <c>10 * tier</c> after playtesting: it roughly doubles tier-1 loot
/// value (a sale/convert that read as pocket lint now funds a real
/// purchase after a short grind) while barely moving the high end.
///
/// "Tier" is continuous: <see cref="Mutants.Core.Time.TimeScale"/> maps a
/// year to a fractional tier, so every function has a <c>double</c>
/// overload used by the world generator. The <c>int</c> overloads round
/// the continuous result and are kept for callers/fixtures working with
/// whole tiers.
/// </summary>
public static class LootScaling
{
    public static double TierBaseValue(double tier)
    {
        if (tier < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(tier), tier, "Tier must be at least 1.");
        }

        return 12 * tier + 10;
    }

    /// <summary>Final item value = tier baseline, modulated by rarity — docs/GDD.md §5.</summary>
    public static double ValueFor(double tier, Rarity rarity) =>
        TierBaseValue(tier) * rarity.ValueMultiplier();

    /// <summary>
    /// Combat stat baseline for a weapon's AttackBonus or armor's
    /// DefenseBonus — same tier/rarity shape as <see cref="ValueFor"/> but
    /// a smaller base so gear bonuses stay in scale with the flat combat
    /// numbers in <c>Mutants.Engine.CombatResolver</c>. Not GDD-specified;
    /// original tuning pending Design Agent sign-off.
    /// </summary>
    public static double CombatBonusFor(double tier, Rarity rarity) =>
        2 * tier * rarity.ValueMultiplier();

    public static int TierBaseValue(int tier) => Round(TierBaseValue((double)tier));

    public static int ValueFor(int tier, Rarity rarity) => Round(ValueFor((double)tier, rarity));

    public static int CombatBonusFor(int tier, Rarity rarity) => Round(CombatBonusFor((double)tier, rarity));

    // Plain Math.Round (banker's rounding) so the int overloads match the
    // previous (int)Math.Round(...) behaviour exactly.
    private static int Round(double value) => (int)Math.Round(value);
}
