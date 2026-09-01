using ChronoTravelers.Core.Stats;

namespace ChronoTravelers.Core.Classes;

/// <summary>
/// Per-class growth/scaling constants. The class roster, roles, and primary
/// stats are per docs/GDD.md §4 (partially [SOURCE]); the specific numeric
/// values here (base HP/Tachyons, per-level growth, Tachyon drain multiplier) are
/// NOT in the GDD — they are original placeholder tuning filling the gap
/// noted in docs/GDD.md §4.3 ("Tachyon pools and drain rates differ per class").
/// Per docs/AGENTS.md, these are exactly the kind of tunable numbers that
/// should move into a ChronoTravelers.Content data file (and get Design Agent
/// sign-off) once that project exists — flagged here rather than hidden.
/// </summary>
public sealed record ClassDefinition(
    CharacterClass Class,
    PrimaryStat PrimaryStat,
    StatBlock BaseStats,
    int BaseHp,
    int HpPerLevel,
    int BaseTachyons,
    int TachyonsPerLevel,
    double TachyonDrainMultiplier)
{
    /// <summary>All five class definitions, keyed by <see cref="CharacterClass"/>.</summary>
    public static readonly IReadOnlyDictionary<CharacterClass, ClassDefinition> All =
        // TachyonsPerLevel bumped +1 across the board after playtesting: the
        // pool grew too slowly to keep pace with travel + heal + cast all
        // drawing on it (paired with the cheaper travel coefficient).
        new Dictionary<CharacterClass, ClassDefinition>
        {
            [CharacterClass.Soldier] = new(
                CharacterClass.Soldier, PrimaryStat.Strength,
                BaseStats: new StatBlock(Strength: 15, Agility: 10, Resolve: 8, Intellect: 8),
                BaseHp: 30, HpPerLevel: 6,
                BaseTachyons: 20, TachyonsPerLevel: 4,
                TachyonDrainMultiplier: 0.8),

            [CharacterClass.Spy] = new(
                CharacterClass.Spy, PrimaryStat.Agility,
                BaseStats: new StatBlock(Strength: 9, Agility: 15, Resolve: 8, Intellect: 10),
                BaseHp: 24, HpPerLevel: 5,
                BaseTachyons: 24, TachyonsPerLevel: 4,
                TachyonDrainMultiplier: 0.9),

            // Doctor/Scientist BaseHp nudged up (22→24, 18→21) — playtests
            // had both wiping in the first year to a single bad opening
            // exchange. Doctor's HpPerLevel also bumped (4→5, matching
            // Spy) — the BaseHp bump alone still left it losing early
            // fights it should've had the margin for; growing the whole
            // curve fits its "keeps you and allies standing" identity
            // better than a one-time early buffer that fades by mid-game.
            // Scientist's HpPerLevel is untouched — it's meant to stay the
            // squishiest caster past the opening.
            [CharacterClass.Doctor] = new(
                CharacterClass.Doctor, PrimaryStat.Resolve,
                BaseStats: new StatBlock(Strength: 9, Agility: 8, Resolve: 15, Intellect: 10),
                BaseHp: 24, HpPerLevel: 5,
                BaseTachyons: 30, TachyonsPerLevel: 4,
                TachyonDrainMultiplier: 1.0),

            [CharacterClass.Scientist] = new(
                CharacterClass.Scientist, PrimaryStat.Intellect,
                BaseStats: new StatBlock(Strength: 7, Agility: 9, Resolve: 8, Intellect: 16),
                BaseHp: 21, HpPerLevel: 3,
                BaseTachyons: 34, TachyonsPerLevel: 5,
                TachyonDrainMultiplier: 1.3),

            [CharacterClass.Engineer] = new(
                CharacterClass.Engineer, PrimaryStat.Intellect,
                BaseStats: new StatBlock(Strength: 7, Agility: 10, Resolve: 9, Intellect: 15),
                BaseHp: 18, HpPerLevel: 3,
                BaseTachyons: 32, TachyonsPerLevel: 5,
                TachyonDrainMultiplier: 1.2),
        };

    public static ClassDefinition For(CharacterClass characterClass) => All[characterClass];

    /// <summary>
    /// Level at which HP growth halves. Below it each level adds the full
    /// <see cref="HpPerLevel"/>; at and above it, half (rounded down). A
    /// flat-linear pool ran away from what a deep-future monster could ever
    /// threaten — by the level cap a Soldier had ~10× base HP — so the far
    /// end tapers while the early/mid game (≤ this level) is untouched
    /// (playtest feedback).
    /// </summary>
    public const int HpGrowthKneeLevel = 15;

    public int MaxHpAtLevel(int level)
    {
        var levelsAbove1 = Math.Max(0, level - 1);
        var atFullRate = Math.Min(levelsAbove1, HpGrowthKneeLevel - 1);
        var atHalfRate = levelsAbove1 - atFullRate;
        return BaseHp + HpPerLevel * atFullRate + HpPerLevel * atHalfRate / 2;
    }

    public int MaxTachyonsAtLevel(int level) => BaseTachyons + TachyonsPerLevel * (level - 1);
}
