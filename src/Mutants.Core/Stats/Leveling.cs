namespace Mutants.Core.Stats;

/// <summary>
/// XP curve, level cap, and ability-tier gating per docs/GDD.md §4.1–4.2.
/// The soft level cap formula and the "every 5th level unlocks an ability
/// tier, 6 tiers total" pattern are GDD-specified; the exact XP-per-level
/// curve is original tuning (not in the GDD) filling that gap.
/// </summary>
public static class Leveling
{
    public const int MaxCharacterLevel = 30;
    public const int LevelsPerAbilityTier = 5;
    public const int AbilityTierCount = 6;

    /// <summary>
    /// Cumulative XP required to reach <paramref name="level"/> from level 1.
    /// Quadratic curve (steepens with level) — original tuning.
    /// </summary>
    public static int CumulativeXpForLevel(int level)
    {
        if (level < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Level must be at least 1.");
        }

        // Level 1 requires 0 XP; each subsequent level costs 100 * (level - 1) more
        // than a flat baseline, giving a smoothly increasing (triangular-number) curve.
        var levelsAbove1 = level - 1;
        return 100 * levelsAbove1 * (levelsAbove1 + 1) / 2;
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

    /// <summary>True on levels 5, 10, 15, 20, 25, 30 — a new ability tier unlocks.</summary>
    public static bool UnlocksAbilityTier(int level) =>
        level > 0 && level <= MaxCharacterLevel && level % LevelsPerAbilityTier == 0;

    /// <summary>Which ability tier (1–6) a level unlocks, or null if it doesn't unlock one.</summary>
    public static int? AbilityTierUnlockedAt(int level) =>
        UnlocksAbilityTier(level) ? level / LevelsPerAbilityTier : null;

    /// <summary>How many ability tiers (0–6) are unlocked for a character at this level.</summary>
    public static int UnlockedAbilityTierCount(int level) =>
        Math.Clamp(level / LevelsPerAbilityTier, 0, AbilityTierCount);
}
