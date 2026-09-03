using System.Linq;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Stats;

namespace ChronoTravelers.Core.Monsters;

/// <summary>
/// Tier-to-stat baselines for monsters. docs/GDD.md §5 confirms loot
/// scales with how far into the future a monster is native to, but gives
/// no monster stat formulas — these curves are original tuning pending
/// Design Agent sign-off, kept roughly in scale with
/// <see cref="Items.LootScaling"/> and <see cref="Stats.Leveling"/> so a
/// tier-N monster is a sensible fight for a character around level
/// <c>10 * N</c> (the soft level cap for that point in the timeline).
///
/// "Tier" is a continuous quantity: <see cref="ChronoTravelers.Core.Time.TimeScale"/>
/// maps a year to a fractional tier (year 2000 = tier 1.0 … year 5000 =
/// tier 9.0), so every baseline has a <c>double</c> overload used by the
/// world generator. The <c>int</c> overloads are kept for callers and
/// fixtures working with whole tiers and simply round the continuous
/// result — identical output for integer tiers.
///
/// <para>
/// <b>Second tuning pass (playtest finding, superseding the first).</b> A
/// first pass made <see cref="BaseHp(double)"/> and
/// <see cref="BaseAttackPower(double)"/> superlinear so a level-cap
/// character's attack didn't one-shot a deep monster — but it hand-tuned
/// those curves (and a flat <c>1 + tier</c> for <see cref="BaseDefense(double)"/>)
/// independently of how fast a <em>character's own</em> stats/gear
/// actually grow via <see cref="Stats.Leveling"/> / <see cref="Classes.ClassDefinition"/> /
/// <see cref="Items.LootScaling"/>. They silently diverged: a 5000-run
/// playtest showed every fight, tier 1 through 9, dealt the player only
/// 2–4 damage a hit (max 12) while a Traveler's own hits routinely broke
/// 50, often over 150. The actual numbers: a level-<c>min(60,10·tier)</c>
/// character's own <c>EffectiveDefense</c> (Agility contribution + a
/// standard-power armour piece) already exceeded a same-tier monster's
/// <see cref="BaseAttackPower(double)"/> by tier 2, and stayed 1.7–3×
/// ahead the rest of the way — so real fights spent their entire
/// existence pinned against <see cref="Engine.Combat.CombatResolver"/>'s
/// damage floor, at every tier, not just deep in the timeline as
/// intended. Meanwhile a level-matched character's own attack (primary
/// stat + a standard weapon) roughly matched or exceeded
/// <see cref="BaseHp(double)"/> outright — a same-tier monster died in
/// one hit at <em>every</em> tier, not just the ones a first playtest
/// pass happened to probe.
/// </para>
/// <para>
/// The fix: instead of two independently-guessed polynomials, every
/// baseline below is <b>derived from</b> what a level-matched character
/// with standard (1.0×) gear actually has — <see cref="ReferencePlayerAttack"/>
/// / <see cref="ReferencePlayerDefense"/>, built from the real
/// <see cref="Leveling"/> / <see cref="ClassDefinition"/> / <see cref="LootScaling"/>
/// constants (averaged across the five classes) rather than duplicated
/// magic numbers, so the two sides of combat can't drift apart again
/// without a test failing (see <c>MonsterScalingTests</c>'s reference-ratio
/// checks). <see cref="Engine.Combat.CombatResolver.RollDamage"/> was
/// also reworked from a linear subtract-then-floor into a smooth,
/// ratio-based mitigation curve, so there's no separate "floor" threshold
/// to keep in sync with these numbers either.
/// </para>
/// </summary>
public static class MonsterScaling
{
    /// <summary>
    /// A monster's <see cref="BaseDefense(double)"/> as a fraction of
    /// <see cref="ReferencePlayerAttack"/> at the same tier — how much of
    /// a level-matched character's own hit a monster shrugs off. Tuned
    /// (playtest) so a standard fight is a multi-round exchange rather
    /// than a one-shot; see the type doc comment.
    /// </summary>
    public const double DefenseFractionOfPlayerAttack = 0.30;

    /// <summary>
    /// A monster's <see cref="BaseAttackPower(double)"/> is
    /// <see cref="ReferencePlayerDefense"/> at the same tier divided by
    /// this — i.e. a level-matched character's own defence mitigates
    /// roughly this fraction of an incoming hit. Below 1.0 on purpose: a
    /// monster should hit meaningfully harder than the player's defence
    /// alone fully absorbs, so incoming damage stays a real cost instead
    /// of converging to whatever <see cref="Engine.Combat.CombatResolver"/>'s
    /// mitigation floor happens to be.
    /// </summary>
    public const double AttackToPlayerDefenseRatio = 0.70;

    /// <summary>
    /// How many of a level-matched character's own (mitigated) hits it
    /// takes to drop a regular monster — <see cref="BaseHp(double)"/> is
    /// set to exactly this many. 3.5 made the arrival-year 1v1 a photo
    /// finish for a fresh level-1 (kills in 4–5 rounds, dies in 4–5);
    /// 3.0 shortens it enough that a level-1 with the starter weapon wins
    /// the opener with margin, while a deeper fight stays multi-round.
    /// </summary>
    public const double HitsToKillMonster = 3.0;

    /// <summary>
    /// <see cref="Characters.Traveler.EffectiveDefense"/>'s Agility
    /// divisor — kept here (rather than duplicated as a private constant
    /// on <c>Traveler</c>) since this file is the single source of truth
    /// for how monster and player combat stats stay proportionate; see
    /// the type doc comment. Was 2 outright; 2.5 (playtest) trims how much
    /// of a level-matched character's per-level Agility growth converts
    /// into free defence, since that growth compounds over up to 59
    /// level-ups and dwarfs anything <see cref="BaseAttackPower(double)"/>
    /// can threaten it with otherwise.
    /// </summary>
    public const double AgilityToDefenseDivisor = 2.5;

    /// <summary>The five classes' primary-stat base value, averaged — the representative "level 1" attack stat a monster's curve is calibrated against.</summary>
    private static readonly double AveragePrimaryStatBase =
        ClassDefinition.All.Values.Average(c => c.BaseStats.Get(c.PrimaryStat));

    /// <summary>The five classes' base Agility, averaged — the representative "level 1" defence-driving stat.</summary>
    private static readonly double AverageAgilityBase =
        ClassDefinition.All.Values.Average(c => c.BaseStats.Agility);

    /// <summary>
    /// Agility's per-level growth, averaged across the five classes: the
    /// Spy (whose primary stat *is* Agility) gets
    /// <see cref="Leveling.PrimaryStatGainPerLevel"/> on it; the other
    /// four get <see cref="Leveling.SecondaryStatGainPerLevel"/>.
    /// </summary>
    private static readonly double AverageAgilityGrowthPerLevel =
        ((ClassDefinition.All.Count - 1) * Leveling.SecondaryStatGainPerLevel + Leveling.PrimaryStatGainPerLevel)
        / (double)ClassDefinition.All.Count;

    /// <summary>
    /// The character level a tier-<paramref name="tier"/> monster is
    /// calibrated against. Ramps linearly across the whole timeline: tier
    /// 1.0 (year 2000, where a Traveler arrives at level 1) → level 1,
    /// tier 9.0 (year 5000) → the hard cap. A first cut pinned this at
    /// <c>10·tier</c> — the soft-cap pairing — but that made year 2000
    /// monsters expect a level-10 character, so a fresh level-1 start
    /// couldn't win (or survive) a single fight in its own arrival year
    /// (playtest). Anchoring the low end at the actual starting level
    /// keeps the early game fightable while the far future still scales
    /// to a capped character.
    /// </summary>
    private static double ReferenceLevel(double tier) =>
        Math.Clamp(1 + (tier - 1) * (Leveling.MaxCharacterLevel - 1) / 8.0, 1, Leveling.MaxCharacterLevel);

    /// <summary>
    /// A level-matched character's own attack power with a standard
    /// (1.0×) weapon: average primary-stat base, grown at
    /// <see cref="Leveling.PrimaryStatGainPerLevel"/> to <see cref="ReferenceLevel"/>,
    /// plus a standard weapon's AttackBonus (<see cref="LootScaling.EquipBonusFor(double, double)"/>)
    /// at this tier. What <see cref="BaseDefense(double)"/> and
    /// <see cref="BaseHp(double)"/> are calibrated against.
    /// </summary>
    private static double ReferencePlayerAttack(double tier) =>
        AveragePrimaryStatBase + Leveling.PrimaryStatGainPerLevel * (ReferenceLevel(tier) - 1)
        + LootScaling.EquipBonusFor(tier, 1.0);

    /// <summary>
    /// A level-matched character's own <see cref="Characters.Traveler.EffectiveDefense"/>
    /// with a standard (1.0×) armour piece: average base Agility, grown at
    /// <see cref="AverageAgilityGrowthPerLevel"/> to <see cref="ReferenceLevel"/>,
    /// divided by <see cref="AgilityToDefenseDivisor"/>, plus a standard
    /// armour piece's DefenseBonus (<see cref="LootScaling.ArmorEquipBonusFor(double, double)"/>)
    /// at this tier. What <see cref="BaseAttackPower(double)"/> is
    /// calibrated against.
    /// </summary>
    private static double ReferencePlayerDefense(double tier) =>
        (AverageAgilityBase + AverageAgilityGrowthPerLevel * (ReferenceLevel(tier) - 1)) / AgilityToDefenseDivisor
        + LootScaling.ArmorEquipBonusFor(tier, 1.0);

    /// <summary>
    /// The same ratio-based mitigation <see cref="Engine.Combat.CombatResolver.RollDamage"/>
    /// applies to a real hit (before variance) — used here purely to size
    /// <see cref="BaseHp(double)"/> against how much of a level-matched
    /// character's own attack actually lands once <see cref="BaseDefense(double)"/>
    /// mitigates it.
    /// </summary>
    private static double Mitigate(double attack, double defense) =>
        attack + defense > 0 ? attack * attack / (attack + defense) : attack;

    /// <summary>
    /// A regular monster's max HP: exactly <see cref="HitsToKillMonster"/>
    /// of a level-matched character's own mitigated hits (see the type
    /// doc comment for why this replaced an independently-tuned
    /// polynomial).
    /// </summary>
    public static double BaseHp(double tier) =>
        Require(tier, Mitigate(ReferencePlayerAttack(tier), BaseDefense(tier)) * HitsToKillMonster);

    /// <summary>
    /// A regular monster's attack power: <see cref="ReferencePlayerDefense"/>
    /// at this tier scaled up by <see cref="AttackToPlayerDefenseRatio"/>,
    /// so a level-matched character's own defence mitigates a real but
    /// not overwhelming fraction of each hit (see the type doc comment).
    /// </summary>
    public static double BaseAttackPower(double tier) =>
        Require(tier, ReferencePlayerDefense(tier) / AttackToPlayerDefenseRatio);

    /// <summary>
    /// A regular monster's defence: <see cref="DefenseFractionOfPlayerAttack"/>
    /// of a level-matched character's own attack at this tier — real
    /// mitigation on the player's hits without turning a regular monster
    /// into a bullet sponge (see the type doc comment).
    /// </summary>
    public static double BaseDefense(double tier) =>
        Require(tier, DefenseFractionOfPlayerAttack * ReferencePlayerAttack(tier));

    public static double BaseSpeed(double tier) => Require(tier, 8 + tier);

    /// <summary>XP reward for defeating a tier-N monster — deliberately generous relative to <see cref="Stats.Leveling.CumulativeXpForLevel"/> so a handful of kills advances a level.</summary>
    public static double XpReward(double tier) => Require(tier, 40 * tier);

    /// <summary>Fraction of kill XP lost per character level past a tier's band cap (<c>10 × tier</c> — the top of what that content is meant for).</summary>
    public const double OutlevelXpFalloffPerLevel = 0.08;

    /// <summary>A kill never yields less than this fraction of its base XP, however far you've outgrown the content.</summary>
    public const double MinKillXpFraction = 0.10;

    /// <summary>
    /// Kill XP actually granted to a level-<paramref name="killerLevel"/>
    /// character for defeating a tier-<paramref name="monsterTier"/> monster
    /// worth <paramref name="baseXp"/>. Full value while the killer is
    /// within (or below) the band that tier is meant for — a tier-N monster
    /// is a fair fight up to about level <c>10 × N</c> (see the type
    /// summary) — then it falls off <see cref="OutlevelXpFalloffPerLevel"/>
    /// per level past that cap, down to a <see cref="MinKillXpFraction"/>
    /// floor. So grinding a year long after you've outgrown it trickles,
    /// and the XP is out where the fight is still real. Never below 1.
    /// </summary>
    public static int KillXp(int baseXp, int monsterTier, int killerLevel)
    {
        var over = killerLevel - 10 * monsterTier;
        if (over <= 0)
        {
            return baseXp;
        }

        var factor = Math.Clamp(1.0 - OutlevelXpFalloffPerLevel * over, MinKillXpFraction, 1.0);
        return Math.Max(1, (int)Math.Round(baseXp * factor));
    }

    /// <summary>Tachyon pool for a tier-N monster — deliberately smaller than a player's (a monster uses it for the odd <c>heal</c>, not as a deep resource). Original tuning.</summary>
    public static double BaseTachyons(double tier) => Require(tier, 8 + 4 * tier);

    public static int BaseHp(int tier) => Round(BaseHp((double)tier));

    public static int BaseAttackPower(int tier) => Round(BaseAttackPower((double)tier));

    public static int BaseDefense(int tier) => Round(BaseDefense((double)tier));

    public static int BaseSpeed(int tier) => Round(BaseSpeed((double)tier));

    public static int XpReward(int tier) => Round(XpReward((double)tier));

    public static int BaseTachyons(int tier) => Round(BaseTachyons((double)tier));

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
