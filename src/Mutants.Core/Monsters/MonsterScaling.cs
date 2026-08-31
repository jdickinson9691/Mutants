namespace Mutants.Core.Monsters;

/// <summary>
/// Tier-to-stat baselines for monsters. docs/GDD.md §5 confirms loot
/// scales with how far into the future a monster is native to, but gives
/// no monster stat formulas — these curves are original tuning pending
/// Design Agent sign-off, kept roughly in scale with
/// <see cref="Items.LootScaling"/> and <see cref="Stats.Leveling"/> so a
/// tier-N monster is a sensible fight for a character around level
/// <c>10 * N</c> (the soft level cap for that point in the timeline).
///
/// "Tier" is a continuous quantity: <see cref="Mutants.Core.Time.TimeScale"/>
/// maps a year to a fractional tier (year 2000 = tier 1.0 … year 5000 =
/// tier 9.0), so every baseline has a <c>double</c> overload used by the
/// world generator. The <c>int</c> overloads are kept for callers and
/// fixtures working with whole tiers and simply round the continuous
/// result — identical output for integer tiers.
/// </summary>
public static class MonsterScaling
{
    public static double BaseHp(double tier) => Require(tier, 20 + 8 * tier);

    public static double BaseAttackPower(double tier) => Require(tier, 3 + 2 * tier);

    public static double BaseDefense(double tier) => Require(tier, 1 + tier);

    public static double BaseSpeed(double tier) => Require(tier, 8 + tier);

    /// <summary>XP reward for defeating a tier-N monster — deliberately generous relative to <see cref="Stats.Leveling.CumulativeXpForLevel"/> so a handful of kills advances a level.</summary>
    public static double XpReward(double tier) => Require(tier, 40 * tier);

    /// <summary>Ion pool for a tier-N monster — deliberately smaller than a player's (a monster uses it for the odd <c>heal</c>, not as a deep resource). Original tuning.</summary>
    public static double BaseIons(double tier) => Require(tier, 8 + 4 * tier);

    public static int BaseHp(int tier) => Round(BaseHp((double)tier));

    public static int BaseAttackPower(int tier) => Round(BaseAttackPower((double)tier));

    public static int BaseDefense(int tier) => Round(BaseDefense((double)tier));

    public static int BaseSpeed(int tier) => Round(BaseSpeed((double)tier));

    public static int XpReward(int tier) => Round(XpReward((double)tier));

    public static int BaseIons(int tier) => Round(BaseIons((double)tier));

    // Plain Math.Round (banker's rounding) so the int overloads are a
    // byte-for-byte no-op refactor of the previous integer arithmetic.
    private static int Round(double value) => (int)Math.Round(value);

    private static double Require(double tier, double value)
    {
        if (tier < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(tier), tier, "Tier must be at least 1.");
        }

        return value;
    }
}
