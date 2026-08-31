using Mutants.Core.Stats;

namespace Mutants.Core.Time;

/// <summary>
/// The continuous timeline the game now runs on — docs/GDD.md §3.2. The
/// player starts in year <see cref="MinYear"/> (2000 A.D.) and can
/// <c>travel</c> to any year up to <see cref="MaxYear"/> (5000). There
/// are no discrete "levels" any more: difficulty is a smooth function of
/// the year.
///
/// <see cref="TierForYear"/> is the bridge to the existing tuning curves
/// (<see cref="Monsters.MonsterScaling"/>, <see cref="Items.LootScaling"/>,
/// which now take a fractional tier): year 2000 → tier 1.0, and every
/// <see cref="YearsPerTier"/> years advances the tier by 1, so year 5000
/// → tier 9.0. That keeps a year's monsters/loot in the same power band
/// the old level-N content occupied (old level N ≈ year
/// <c>2000 + (N-1) * 375</c>).
/// </summary>
public static class TimeScale
{
    public const int MinYear = 2000;
    public const int MaxYear = 5000;

    /// <summary>Years of the timeline that correspond to one tier of scaling.</summary>
    public const double YearsPerTier = 375.0;

    /// <summary>True if <paramref name="year"/> is a travellable year (2000–5000 inclusive).</summary>
    public static bool IsValidYear(int year) => year >= MinYear && year <= MaxYear;

    /// <summary>
    /// The fractional scaling tier for <paramref name="year"/> — 1.0 at
    /// year 2000, rising by 1 every <see cref="YearsPerTier"/> years to
    /// 9.0 at year 5000. Feeds the <c>double</c> overloads on
    /// <see cref="Monsters.MonsterScaling"/> / <see cref="Items.LootScaling"/>.
    /// </summary>
    public static double TierForYear(int year)
    {
        RequireInRange(year);
        return 1 + (year - MinYear) / YearsPerTier;
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
