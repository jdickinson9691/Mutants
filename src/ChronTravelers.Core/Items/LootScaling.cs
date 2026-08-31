namespace ChronTravelers.Core.Items;

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
/// "Tier" is continuous: <see cref="ChronTravelers.Core.Time.TimeScale"/> maps a
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

    /// <summary>Widest and narrowest a weapon/armour's <c>powerMultiplier</c> is allowed to be (a "Standard" piece is 1.0).</summary>
    public const double MinPowerMultiplier = 0.3;

    public const double MaxPowerMultiplier = 3.0;

    /// <summary>
    /// A weapon's AttackBonus / armour's DefenseBonus / ranged weapon's
    /// AttackBonus at <paramref name="tier"/>, given the archetype's
    /// <paramref name="powerMultiplier"/>. The per-tier baseline
    /// (<c>4.4·tier + 1</c>, a Standard 1.0× piece) is multiplied by the
    /// clamped power multiplier, so a crude 0.5× weapon does about half a
    /// baseline hit and a relic 3.5× one does several times it —
    /// <see cref="Rarity.ForPower"/> then names the band. Not GDD-specified;
    /// original tuning pending Design Agent sign-off.
    /// </summary>
    public static double EquipBonusFor(double tier, double powerMultiplier)
    {
        if (tier < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(tier), tier, "Tier must be at least 1.");
        }

        var mult = Math.Clamp(powerMultiplier, MinPowerMultiplier, MaxPowerMultiplier);
        return (4.4 * tier + 1) * mult;
    }

    /// <summary>
    /// Back-compat shim: a combat bonus keyed by rarity rather than a raw
    /// multiplier, for <see cref="Item.Create"/> and sandbox fixtures.
    /// Each rarity maps to the mid-point multiplier of its
    /// <see cref="Rarity.ForPower"/> band, so it round-trips.
    /// </summary>
    public static double CombatBonusFor(double tier, Rarity rarity) =>
        EquipBonusFor(tier, RepresentativeMultiplier(rarity));

    /// <summary>The <c>powerMultiplier</c> a rarity stands for — the inverse of <see cref="Rarity.ForPower"/> at each band's centre.</summary>
    public static double RepresentativeMultiplier(Rarity rarity) => rarity switch
    {
        Rarity.Common => 0.6,
        Rarity.Uncommon => 1.0,
        Rarity.Rare => 1.6,
        Rarity.Epic => 2.2,
        Rarity.Legendary => 2.8,
        _ => throw new ArgumentOutOfRangeException(nameof(rarity), rarity, null),
    };

    public static int TierBaseValue(int tier) => Round(TierBaseValue((double)tier));

    public static int ValueFor(int tier, Rarity rarity) => Round(ValueFor((double)tier, rarity));

    public static int CombatBonusFor(int tier, Rarity rarity) => Round(CombatBonusFor((double)tier, rarity));

    public static int EquipBonusFor(int tier, double powerMultiplier) => Round(EquipBonusFor((double)tier, powerMultiplier));

    // Plain Math.Round (banker's rounding) so the int overloads match the
    // previous (int)Math.Round(...) behaviour exactly.
    private static int Round(double value) => (int)Math.Round(value);
}
