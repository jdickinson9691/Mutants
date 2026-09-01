namespace ChronoTravelers.Core.Stats;

/// <summary>
/// XP curve, level cap, and ability-tier gating per docs/GDD.md §4.1–4.2.
/// The soft level cap formula and the "every 5th level unlocks an ability
/// tier, 6 tiers total" pattern are GDD-specified; the exact XP-per-level
/// curve is original tuning (not in the GDD) filling that gap.
/// </summary>
public static class Leveling
{
    /// <summary>
    /// The character-level ceiling. Raised 30 → 60 so a Traveler keeps
    /// growing across the whole 2000–5000 A.D. timeline instead of maxing
    /// out at year ~2500 (see <see cref="Time.TimeScale.SoftLevelCapForYear"/>,
    /// docs/GDD.md §4.1). Levels 31–60 grant stat + HP/Tachyon growth; the
    /// ability trees still stop at their 6th tier (level 30 / Engineer 21)
    /// until they're extended — docs/GDD.md §4.2.
    /// </summary>
    public const int MaxCharacterLevel = 60;

    public const int LevelsPerAbilityTier = 5;
    public const int AbilityTierCount = 6;

    /// <summary>The last level that unlocks an ability tier — <see cref="AbilityTierCount"/> × <see cref="LevelsPerAbilityTier"/>. Levels past this grow stats but no new abilities (yet).</summary>
    public const int TopAbilityLevel = AbilityTierCount * LevelsPerAbilityTier;

    /// <summary>Where the XP-per-level cost stops rising and holds flat — so levels 26–60 are a reachable grind, not a quadratic wall. Levels 1–25 are unchanged from the pre-cap-60 curve.</summary>
    public const int XpCurveKneeLevel = 25;

    /// <summary>Points added to the class's primary stat on each level-up.</summary>
    public const int PrimaryStatGainPerLevel = 2;

    /// <summary>Points added to each of the other three stats on each level-up.</summary>
    public const int SecondaryStatGainPerLevel = 1;

    /// <summary>
    /// Cumulative XP required to reach <paramref name="level"/> from level 1.
    /// Quadratic (each level costs <c>100 · (level-1)</c> more than the last)
    /// through <see cref="XpCurveKneeLevel"/>, then a flat per-level cost of
    /// <c>100 · (XpCurveKneeLevel-1)</c> after — so the raised cap doesn't
    /// turn the deep game into a quadratic grind wall. Levels 1–25 are
    /// identical to the pre-cap-60 curve. Original tuning.
    /// </summary>
    public static int CumulativeXpForLevel(int level)
    {
        if (level < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Level must be at least 1.");
        }

        // Triangular-number ramp: reaching level L costs sum(100·k) for k in 1..L-1.
        static int Ramp(int lvl) => 100 * (lvl - 1) * lvl / 2;

        if (level <= XpCurveKneeLevel)
        {
            return Ramp(level);
        }

        // Past the knee: the per-level cost stops rising and holds at what
        // the next quadratic step (25→26) would have been — a smooth join.
        var flatPerLevel = 100 * XpCurveKneeLevel;
        return Ramp(XpCurveKneeLevel) + flatPerLevel * (level - XpCurveKneeLevel);
    }

    /// <summary>
    /// docs/GDD.md §4.1: "a soft cap exists at character_level = 10 *
    /// unlocked_time_level." [SOURCE-adjacent: value is GDD-specified, not
    /// historically sourced.]
    /// </summary>
    public static int SoftLevelCap(int unlockedTimeLevel)
    {
        if (unlockedTimeLevel < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(unlockedTimeLevel), unlockedTimeLevel,
                "Unlocked time level must be at least 1.");
        }

        return Math.Min(MaxCharacterLevel, 10 * unlockedTimeLevel);
    }

    /// <summary>True on levels 5, 10, 15, 20, 25, 30 — a new ability tier unlocks. Levels past <see cref="TopAbilityLevel"/> grow stats only, so they return false even though they're valid levels.</summary>
    public static bool UnlocksAbilityTier(int level) =>
        level > 0 && level <= TopAbilityLevel && level % LevelsPerAbilityTier == 0;

    /// <summary>Which ability tier (1–6) a level unlocks, or null if it doesn't unlock one.</summary>
    public static int? AbilityTierUnlockedAt(int level) =>
        UnlocksAbilityTier(level) ? level / LevelsPerAbilityTier : null;

    /// <summary>How many ability tiers (0–6) are unlocked for a character at this level.</summary>
    public static int UnlockedAbilityTierCount(int level) =>
        Math.Clamp(level / LevelsPerAbilityTier, 0, AbilityTierCount);
}
