using ChronoTravelers.Core.Stats;

namespace ChronoTravelers.Core.Time;

/// <summary>
/// The continuous timeline the game now runs on — docs/GDD.md §3.2. The
/// player starts in year <see cref="MinYear"/> (2000 A.D.) and can
/// <c>travel</c> to any year up to <see cref="MaxYear"/> (5000). There
/// are no discrete "levels" any more: difficulty is a smooth function of
/// the year.
///
/// <see cref="TierForYear"/> is the bridge to the existing tuning curves
/// (<see cref="Monsters.MonsterScaling"/>, <see cref="Items.LootScaling"/>,
/// which now take a fractional tier): year 2000 → tier 1.0, year 5000 →
/// tier 9.0. The curve is deliberately <em>steeper early</em> — one tier
/// per <see cref="EarlyYearsPerTier"/> years through the first millennium
/// (2000–<see cref="KneeYear"/>, tiers 1→<see cref="KneeTier"/>), then a
/// gentler <see cref="LateYearsPerTier"/> years per tier after — so a
/// modest early hop actually changes the fight, instead of the whole
/// 2000–2600 span playing identically (playtest feedback).
/// </summary>
public static class TimeScale
{
    public const int MinYear = 2000;
    public const int MaxYear = 5000;

    /// <summary>Legacy reference only: the old flat rate, still used by the schema-1 save migration's level→year mapping.</summary>
    public const double YearsPerTier = 375.0;

    /// <summary>Where the difficulty curve changes slope.</summary>
    public const int KneeYear = 3000;

    /// <summary>The tier reached at <see cref="KneeYear"/>.</summary>
    public const double KneeTier = 5.0;

    /// <summary>Years per tier before the knee — the steep early stretch.</summary>
    public const double EarlyYearsPerTier = 250.0;

    /// <summary>Years per tier after the knee — the gentler late stretch (KneeTier..9.0 across KneeYear..MaxYear).</summary>
    public const double LateYearsPerTier = 500.0;

    /// <summary>True if <paramref name="year"/> is a travellable year (2000–5000 inclusive).</summary>
    public static bool IsValidYear(int year) => year >= MinYear && year <= MaxYear;

    /// <summary>
    /// The fractional scaling tier for <paramref name="year"/> — 1.0 at
    /// year 2000, <see cref="KneeTier"/> at <see cref="KneeYear"/>, 9.0 at
    /// year 5000, piecewise-linear with a steeper early slope. Feeds the
    /// <c>double</c> overloads on <see cref="Monsters.MonsterScaling"/> /
    /// <see cref="Items.LootScaling"/>.
    /// </summary>
    public static double TierForYear(int year)
    {
        RequireInRange(year);

        return year <= KneeYear
            ? 1 + (year - MinYear) / EarlyYearsPerTier
            : KneeTier + (year - KneeYear) / LateYearsPerTier;
    }

    /// <summary>
    /// The soft character-level cap for a character whose furthest year
    /// reached is <paramref name="year"/> — the continuous analog of
    /// docs/GDD.md §4.1's "10 * unlocked_time_level", clamped to
    /// [10, <see cref="Leveling.MaxCharacterLevel"/>]. Soft: grinding past
    /// it is allowed, it just gates fast-forwarded NPC spawns and framing.
    /// </summary>
    public static int SoftLevelCapForYear(int year) =>
        Math.Clamp((int)Math.Round(10 * TierForYear(year)), 10, Leveling.MaxCharacterLevel);

    private static void RequireInRange(int year)
    {
        if (!IsValidYear(year))
        {
            throw new ArgumentOutOfRangeException(
                nameof(year), year, $"Year must be between {MinYear} and {MaxYear}.");
        }
    }
}
