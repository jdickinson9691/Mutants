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
        //
        // BaseTachyons then tripled (+200%) and TachyonsPerLevel raised ×6
        // (+500%): the pool was too thin end-to-end for a downstream push —
        // a botched overreach couldn't afford the retreat, so a bad jump
        // turned into a no-fuel death spiral, and the per-level growth never
        // opened the gap back up. With both bumped, a mid-game Traveler
        // carries a real travel/heal buffer (e.g. a level-10 Soldier's
        // nominal pool is 100 + 24·9 = 316). The pool is uncapped anyway;
        // these only move the starting fill and the passive-regen ceiling.
        //
        // BaseTachyons then given a further flat +40 to every class: a
        // fresh level-1 that clears the arrival area heals ~30 HP after
        // each fight (~10 Tachyons at 3:1), and the pre-bump ~60–100 pool
        // ran dry after ~5–6 fights — before the level-ups that grow it.
        // Every cold-start playtest death that got past the opener ended
        // at 0–3 Tachyons. +40 buys the extra fights to reach level 5.
        new Dictionary<CharacterClass, ClassDefinition>
        {
            [CharacterClass.Soldier] = new(
                CharacterClass.Soldier, PrimaryStat.Strength,
                BaseStats: new StatBlock(Strength: 15, Agility: 10, Resolve: 8, Intellect: 8),
                BaseHp: 30, HpPerLevel: 9,
                BaseTachyons: 100, TachyonsPerLevel: 24,  // base 20 x3 + 40, per-level 4 x6
                TachyonDrainMultiplier: 0.8),

            [CharacterClass.Spy] = new(
                CharacterClass.Spy, PrimaryStat.Agility,
                BaseStats: new StatBlock(Strength: 9, Agility: 15, Resolve: 8, Intellect: 10),
                BaseHp: 29, HpPerLevel: 7,
                BaseTachyons: 112, TachyonsPerLevel: 24,  // base 24 x3 + 40, per-level 4 x6
                TachyonDrainMultiplier: 0.9),

            // HpPerLevel raised across the board, spread by class identity so
            // the roster's durability order is preserved, just steeper:
            //   Soldier 6→9  (station security — heaviest gear, the tank)
            //   Spy     5→7  (skirmisher — frontline, but leans on evasion)
            //   Doctor  5→7  (trauma medicine — "keeps you and allies standing")
            //   Engineer 3→5 (frail systems class, but has to live to L5 for its kit)
            //   Scientist 3→4 (glass cannon — smallest bump, stays the squishiest)
            // BaseHp: the four non-Soldier classes were pulled up into the
            // 28–30 band (Spy/Doctor 24→29/30, Scientist 21→28, Engineer
            // 18→28). With MonsterScaling's reference level now ramping from
            // 1 at tier 1 (see its doc comment), a fresh L1 in year 2000
            // takes survivable per-hit damage — but an 18–24 HP pool still
            // couldn't outlast the chip damage of clearing an opening room
            // (playtest: every frail-class cold start wiped at L1 before its
            // first level-up). HpPerLevel and EffectiveDefense keep the
            // classes distinct from here up; this is just the L1 floor.
            [CharacterClass.Doctor] = new(
                CharacterClass.Doctor, PrimaryStat.Resolve,
                BaseStats: new StatBlock(Strength: 9, Agility: 8, Resolve: 15, Intellect: 10),
                BaseHp: 30, HpPerLevel: 7,
                BaseTachyons: 130, TachyonsPerLevel: 24,  // base 30 x3 + 40, per-level 4 x6
                TachyonDrainMultiplier: 1.0),

            [CharacterClass.Scientist] = new(
                CharacterClass.Scientist, PrimaryStat.Intellect,
                BaseStats: new StatBlock(Strength: 7, Agility: 9, Resolve: 8, Intellect: 16),
                BaseHp: 28, HpPerLevel: 4,
                BaseTachyons: 142, TachyonsPerLevel: 30, // base 34 x3 + 40, per-level 5 x6
                TachyonDrainMultiplier: 1.3),

            [CharacterClass.Engineer] = new(
                CharacterClass.Engineer, PrimaryStat.Intellect,
                BaseStats: new StatBlock(Strength: 7, Agility: 10, Resolve: 9, Intellect: 15),
                // 28 -> 30 (up to the Soldier's floor, which it may tie): the
                // Engineer won its opening fight then lost the *next* one on
                // chip damage before its L2 ability unlock (playtest). The
                // low HpPerLevel keeps it the frail systems class from here.
                BaseHp: 30, HpPerLevel: 5,
                BaseTachyons: 136, TachyonsPerLevel: 30,  // base 32 x3 + 40, per-level 5 x6
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
