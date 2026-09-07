using ChronoTravelers.Core.Classes;

namespace ChronoTravelers.Core.Characters;

/// <summary>
/// The mechanical lever a <see cref="PassiveTrait"/> pulls. Each hook is
/// read by whichever system owns that number (<see cref="Traveler"/> for
/// most; <see cref="Economy.Store"/> for the two store hooks;
/// <c>Engine.Npc.MonsterController</c> for the two aggro/ambush hooks that
/// live outside a single fight) via <see cref="PassiveTraits.Sum"/> or
/// <see cref="PassiveTraits.Any"/> — never by switching on the trait's name.
/// Thresholds that are part of a passive's *description* (e.g. Second
/// Wind's "below 30% HP") are deliberately NOT stored here: they're small,
/// class-flavour constants baked into the one call site that reads the
/// hook, matching the GDD wording exactly rather than adding a second
/// tunable per passive.
/// </summary>
public enum PassiveHook
{
    /// <summary>Soldier "Hardened" — % bonus applied on top of equipped armor's DefenseBonus contribution.</summary>
    ArmorDefenseBonusPct,

    /// <summary>Soldier "Second Wind" — % damage reduction on incoming hits while below 30% HP.</summary>
    LowHpDamageReductionPct,

    /// <summary>Soldier "Juggernaut Momentum" — % attack bonus per consecutive round landed this fight (capped at 10 stacks by the reader, not here — see docs/GDD.md §4.2.1's no-miss-mechanic note).</summary>
    ConsecutiveHitAttackBonusPct,

    /// <summary>Soldier "Thick Hide" — % reduction to ambush damage taken.</summary>
    AmbushDamageReductionPct,

    /// <summary>Soldier "Weapon Discipline" / Engineer "Field-Tested Gear" — fraction by which the off-class wield penalty (normally halving effectiveness) is itself reduced.</summary>
    OffClassPenaltyReductionPct,

    /// <summary>Soldier "Unbreakable" — once per fight, a killing blow instead leaves 1 HP.</summary>
    DeathProofOncePerFight,

    /// <summary>Doctor "Bedside Manner" — % bonus to HP restored by <see cref="Traveler.Heal"/>.</summary>
    HealRatioBonusPct,

    /// <summary>Doctor "Resonant Calm" — % damage reduction on hits from an "echo"-tagged attacker.</summary>
    EchoDamageReductionPct,

    /// <summary>Doctor "Steady Hands" — % bonus to HP restored by a consumable item.</summary>
    ConsumableHealBonusPct,

    /// <summary>Doctor "Overwatch" / Scientist "Efficient Circuits" — % faster passive Tachyon regen (fewer ticks per point).</summary>
    TachyonRegenRateBonusPct,

    /// <summary>Doctor "Trauma Ward" — % chance an ambush is negated entirely (no damage, no log of a hit landing).</summary>
    AmbushNegateChancePct,

    /// <summary>Doctor "Vital Reserves" — % of max HP passively regenerated per world tick.</summary>
    MaxHpRegenPerTickPct,

    /// <summary>Spy "Light Fingers" / "Silent Partner" — % discount when buying from a store and % bonus when selling to one; the two Spy entries sharing this hook stack via <see cref="PassiveTraits.Sum"/>.</summary>
    StoreDiscountBonusPct,

    /// <summary>Spy "Quick Reflexes" / Engineer "Overclocked Reflexes" — flat bonus to Speed.</summary>
    FlatSpeedBonus,

    /// <summary>Spy "Opportunist" — % attack damage bonus vs. a target below 40% HP.</summary>
    LowHpTargetAttackBonusPct,

    /// <summary>Spy "Low Profile" — % reduction to aggro gained by nearby monsters.</summary>
    AggroGainReductionPct,

    /// <summary>Spy "Fleet-Footed" / Engineer "Redundant Systems" — % chance to dodge an ambush entirely.</summary>
    AmbushDodgeChancePct,

    /// <summary>Scientist "Tunnel Sense" — % bonus to an item's converted Tachyon value.</summary>
    ConvertValueBonusPct,

    /// <summary>Scientist "Overcurrent" — % attack bonus while at or above 50% of nominal max Tachyons.</summary>
    HighTachyonAttackBonusPct,

    /// <summary>Scientist "Insulated Coils" — % slower Tachyon drain (more ticks per point).</summary>
    TachyonDrainRateReductionPct,

    /// <summary>Scientist "Field Calibration" — % attack damage bonus vs. a "caster"-tagged monster (see <see cref="Time.TimelineContentFactory"/>, which tags every Caster-archetype spawn).</summary>
    CasterDamageBonusPct,

    /// <summary>Scientist "Stable Core" — % chance an ability cast costs no Tachyons.</summary>
    FreeCastChancePct,

    /// <summary>Engineer "Improvised Plating" — flat bonus to Defense.</summary>
    FlatDefenseBonus,

    /// <summary>Engineer "Salvage Sense" — % bonus to convert value for Junk-type items (a Junk item is this game's "scrap" — see docs/GDD.md §4.2.1's implementation note). Junk is convert-only (no store buys it), so this only ever applies via <see cref="Characters.Traveler.Convert"/>.</summary>
    JunkValueBonusPct,

    /// <summary>Engineer "Failsafe Capacitor" — fraction by which a cast's Tachyon cost is reduced when paying full price would drop the pool below 10% of nominal max.</summary>
    LowTachyonCastDiscountPct,

    /// <summary>Soldier "Anomaly Killer" (second wave, level 58) — % attack damage bonus vs. a "paradox"-tagged monster (see <see cref="Time.TimelineContentFactory"/>, which tags every spawn in a paradox-themed era — docs/ENDGAME_STRATEGY.md recommendation 3).</summary>
    ParadoxDamageBonusPct,
}

/// <summary>
/// One always-on passive trait, unlocked automatically at
/// <see cref="Level"/> — no activation, no UI, no AI decision (see
/// docs/GDD.md §4.2.1). Five classes × twelve passives apiece: a first
/// wave (levels 1–28, Engineer 1–19) roughly midway between a pair of
/// consecutive active-ability unlock levels from <c>abilities.json</c>
/// (Engineer's schedule is compressed, matching its own faster
/// active-ability cadence), plus a second wave (levels 31–60, Engineer
/// 31–56) added for the year-3500-5000 endgame (docs/ENDGAME_STRATEGY.md
/// recommendation 1) — smaller top-ups on the same hooks (so a first-wave
/// passive and its second-wave sibling stack via <see cref="PassiveTraits.Sum"/>,
/// same convention as Spy's original two-Level store-discount split) plus
/// one brand-new hook per class where noted.
/// </summary>
public sealed record PassiveTrait(
    CharacterClass Class,
    int Level,
    string Name,
    string Description,
    PassiveHook Hook,
    double Magnitude);

/// <summary>
/// The static passive-trait table — docs/GDD.md §4.2.1 implemented as
/// in-code data rather than a <c>passives.json</c> content file (see
/// docs/CONTENT_PLAN.md's catalog entry for why: passives need no
/// cast-time resolution and no NPC casting decision, unlike
/// <c>abilities.json</c>'s active abilities, so there's no ContentLoader/DTO
/// plumbing worth adding for them).
/// </summary>
public static class PassiveTraits
{
    public static readonly IReadOnlyList<PassiveTrait> All =
    [
        // --- Soldier (passives at 1/8/13/18/23/28, between active levels 5/10/15/20/25/30) ---
        new(CharacterClass.Soldier, 1, "Hardened", "+20% Defense from equipped armor.", PassiveHook.ArmorDefenseBonusPct, 0.20),
        new(CharacterClass.Soldier, 8, "Second Wind", "-10% damage taken while below 30% HP.", PassiveHook.LowHpDamageReductionPct, 0.10),
        new(CharacterClass.Soldier, 13, "Juggernaut Momentum", "+2% attack per consecutive round landed this fight, capped at 10 stacks.", PassiveHook.ConsecutiveHitAttackBonusPct, 0.02),
        new(CharacterClass.Soldier, 18, "Thick Hide", "-25% damage taken from an ambush.", PassiveHook.AmbushDamageReductionPct, 0.25),
        new(CharacterClass.Soldier, 23, "Weapon Discipline", "Off-class weapon penalty halved.", PassiveHook.OffClassPenaltyReductionPct, 0.5),
        new(CharacterClass.Soldier, 28, "Unbreakable", "Once per fight, a killing blow leaves 1 HP instead.", PassiveHook.DeathProofOncePerFight, 1.0),

        // --- Soldier, second wave (docs/ENDGAME_STRATEGY.md rec. 1): passives at 33/38/43/48/53/58 ---
        new(CharacterClass.Soldier, 33, "Reinforced Plating", "+10% Defense from equipped armor (stacks with Hardened).", PassiveHook.ArmorDefenseBonusPct, 0.10),
        new(CharacterClass.Soldier, 38, "Grim Endurance", "-8% damage taken while below 30% HP (stacks with Second Wind).", PassiveHook.LowHpDamageReductionPct, 0.08),
        new(CharacterClass.Soldier, 43, "Battle-Worn Hide", "-15% damage taken from an ambush (stacks with Thick Hide).", PassiveHook.AmbushDamageReductionPct, 0.15),
        new(CharacterClass.Soldier, 48, "Combat Instinct", "15% chance to dodge an ambush entirely.", PassiveHook.AmbushDodgeChancePct, 0.15),
        new(CharacterClass.Soldier, 53, "Battlefield Medic", "Regenerate 0.5% of max HP per world tick (stacks with Vital Reserves-style passives).", PassiveHook.MaxHpRegenPerTickPct, 0.005),
        new(CharacterClass.Soldier, 58, "Anomaly Killer", "+18% attack damage vs. a \"paradox\"-tagged monster.", PassiveHook.ParadoxDamageBonusPct, 0.18),

        // --- Doctor (passives at 1/8/13/18/23/28, between active levels 5/10/15/20/25/30) ---
        new(CharacterClass.Doctor, 1, "Bedside Manner", "+25% HP restored by Heal.", PassiveHook.HealRatioBonusPct, 0.25),
        new(CharacterClass.Doctor, 8, "Resonant Calm", "-15% damage taken from an echo.", PassiveHook.EchoDamageReductionPct, 0.15),
        new(CharacterClass.Doctor, 13, "Steady Hands", "+20% HP restored by consumables.", PassiveHook.ConsumableHealBonusPct, 0.20),
        new(CharacterClass.Doctor, 18, "Overwatch", "+15% Tachyon regen rate.", PassiveHook.TachyonRegenRateBonusPct, 0.15),
        new(CharacterClass.Doctor, 23, "Trauma Ward", "20% chance to negate an ambush entirely.", PassiveHook.AmbushNegateChancePct, 0.20),
        new(CharacterClass.Doctor, 28, "Vital Reserves", "Regenerate 1% of max HP per world tick.", PassiveHook.MaxHpRegenPerTickPct, 0.01),

        // --- Doctor, second wave (docs/ENDGAME_STRATEGY.md rec. 1): passives at 33/38/43/48/53/58 ---
        new(CharacterClass.Doctor, 33, "Advanced Triage", "+12% HP restored by Heal (stacks with Bedside Manner).", PassiveHook.HealRatioBonusPct, 0.12),
        new(CharacterClass.Doctor, 38, "Calming Presence", "-10% damage taken from an echo (stacks with Resonant Calm).", PassiveHook.EchoDamageReductionPct, 0.10),
        new(CharacterClass.Doctor, 43, "Apothecary's Touch", "+12% HP restored by consumables (stacks with Steady Hands).", PassiveHook.ConsumableHealBonusPct, 0.12),
        new(CharacterClass.Doctor, 48, "Deep Reserves", "+12% Tachyon regen rate (stacks with Overwatch).", PassiveHook.TachyonRegenRateBonusPct, 0.12),
        new(CharacterClass.Doctor, 53, "Field Immunity", "+15% chance to negate an ambush entirely (stacks with Trauma Ward).", PassiveHook.AmbushNegateChancePct, 0.15),
        new(CharacterClass.Doctor, 58, "Regenerative Field", "Regenerate an extra 0.75% of max HP per world tick (stacks with Vital Reserves).", PassiveHook.MaxHpRegenPerTickPct, 0.0075),

        // --- Spy (passives at 1/8/13/18/23/28, between active levels 5/10/15/20/25/30) ---
        new(CharacterClass.Spy, 1, "Light Fingers", "5% store discount when buying, 5% bonus when selling.", PassiveHook.StoreDiscountBonusPct, 0.05),
        new(CharacterClass.Spy, 8, "Quick Reflexes", "+3 Speed.", PassiveHook.FlatSpeedBonus, 3),
        new(CharacterClass.Spy, 13, "Opportunist", "+15% attack damage vs. a target below 40% HP.", PassiveHook.LowHpTargetAttackBonusPct, 0.15),
        new(CharacterClass.Spy, 18, "Low Profile", "-20% aggro gained by nearby monsters.", PassiveHook.AggroGainReductionPct, 0.20),
        new(CharacterClass.Spy, 23, "Fleet-Footed", "20% chance to dodge an ambush entirely.", PassiveHook.AmbushDodgeChancePct, 0.20),
        new(CharacterClass.Spy, 28, "Silent Partner", "Light Fingers' store discount/bonus doubles to 10%.", PassiveHook.StoreDiscountBonusPct, 0.05),

        // --- Spy, second wave (docs/ENDGAME_STRATEGY.md rec. 1): passives at 33/38/43/48/53/58 ---
        new(CharacterClass.Spy, 33, "Underworld Ties", "+5% store discount when buying, 5% bonus when selling (stacks further).", PassiveHook.StoreDiscountBonusPct, 0.05),
        new(CharacterClass.Spy, 38, "Hair-Trigger Reflexes", "+2 Speed (stacks with Quick Reflexes).", PassiveHook.FlatSpeedBonus, 2),
        new(CharacterClass.Spy, 43, "Killing Instinct", "+10% attack damage vs. a target below 40% HP (stacks with Opportunist).", PassiveHook.LowHpTargetAttackBonusPct, 0.10),
        new(CharacterClass.Spy, 48, "Ghost Step", "-15% aggro gained by nearby monsters (stacks with Low Profile).", PassiveHook.AggroGainReductionPct, 0.15),
        new(CharacterClass.Spy, 53, "Untraceable", "+15% chance to dodge an ambush entirely (stacks with Fleet-Footed).", PassiveHook.AmbushDodgeChancePct, 0.15),
        new(CharacterClass.Spy, 58, "Broker's Network", "Store discount/bonus climbs another 5%, to 20% total.", PassiveHook.StoreDiscountBonusPct, 0.05),

        // --- Scientist (passives at 1/8/13/18/23/28, between active levels 5/10/15/20/25/30) ---
        new(CharacterClass.Scientist, 1, "Tunnel Sense", "+10% Tachyon value when converting items.", PassiveHook.ConvertValueBonusPct, 0.10),
        new(CharacterClass.Scientist, 8, "Efficient Circuits", "+15% Tachyon regen rate.", PassiveHook.TachyonRegenRateBonusPct, 0.15),
        new(CharacterClass.Scientist, 13, "Overcurrent", "+10% attack while at or above 50% of nominal max Tachyons.", PassiveHook.HighTachyonAttackBonusPct, 0.10),
        new(CharacterClass.Scientist, 18, "Insulated Coils", "+15% slower Tachyon drain.", PassiveHook.TachyonDrainRateReductionPct, 0.15),
        new(CharacterClass.Scientist, 23, "Field Calibration", "+20% attack damage vs. Caster-archetype monsters.", PassiveHook.CasterDamageBonusPct, 0.20),
        new(CharacterClass.Scientist, 28, "Stable Core", "10% chance an ability cast costs no Tachyons.", PassiveHook.FreeCastChancePct, 0.10),

        // --- Scientist, second wave (docs/ENDGAME_STRATEGY.md rec. 1): passives at 33/38/43/48/53/58 ---
        new(CharacterClass.Scientist, 33, "Refined Conversion", "+8% Tachyon value when converting items (stacks with Tunnel Sense).", PassiveHook.ConvertValueBonusPct, 0.08),
        new(CharacterClass.Scientist, 38, "Superconductor Coils", "+12% Tachyon regen rate (stacks with Efficient Circuits).", PassiveHook.TachyonRegenRateBonusPct, 0.12),
        new(CharacterClass.Scientist, 43, "Overcharged Capacitor", "+8% attack while at or above 50% of nominal max Tachyons (stacks with Overcurrent).", PassiveHook.HighTachyonAttackBonusPct, 0.08),
        new(CharacterClass.Scientist, 48, "Reinforced Insulation", "+12% slower Tachyon drain (stacks with Insulated Coils).", PassiveHook.TachyonDrainRateReductionPct, 0.12),
        new(CharacterClass.Scientist, 53, "Precision Calibration", "+12% attack damage vs. Caster-archetype monsters (stacks with Field Calibration).", PassiveHook.CasterDamageBonusPct, 0.12),
        new(CharacterClass.Scientist, 58, "Zero-Point Access", "+8% chance an ability cast costs no Tachyons (stacks with Stable Core).", PassiveHook.FreeCastChancePct, 0.08),

        // --- Engineer (accelerated schedule: passives at 1/4/7/11/15/19, between active levels 2/5/9/13/17/21) ---
        new(CharacterClass.Engineer, 1, "Field-Tested Gear", "Off-class weapon penalty halved.", PassiveHook.OffClassPenaltyReductionPct, 0.5),
        new(CharacterClass.Engineer, 4, "Overclocked Reflexes", "+3 Speed.", PassiveHook.FlatSpeedBonus, 3),
        new(CharacterClass.Engineer, 7, "Redundant Systems", "20% chance to dodge an ambush entirely.", PassiveHook.AmbushDodgeChancePct, 0.20),
        new(CharacterClass.Engineer, 11, "Improvised Plating", "+3 flat Defense.", PassiveHook.FlatDefenseBonus, 3),
        new(CharacterClass.Engineer, 15, "Salvage Sense", "+15% value converting Junk items.", PassiveHook.JunkValueBonusPct, 0.15),
        new(CharacterClass.Engineer, 19, "Failsafe Capacitor", "Halves a cast's Tachyon cost when paying full price would drop the pool below 10% of nominal max.", PassiveHook.LowTachyonCastDiscountPct, 0.5),

        // --- Engineer, second wave (docs/ENDGAME_STRATEGY.md rec. 1, accelerated schedule matching its first wave): passives at 31/36/41/46/51/56 ---
        new(CharacterClass.Engineer, 31, "Layered Armor", "+2 flat Defense (stacks with Improvised Plating).", PassiveHook.FlatDefenseBonus, 2),
        new(CharacterClass.Engineer, 36, "Tuned Servos", "+2 Speed (stacks with Overclocked Reflexes).", PassiveHook.FlatSpeedBonus, 2),
        new(CharacterClass.Engineer, 41, "Backup Protocols", "+15% chance to dodge an ambush entirely (stacks with Redundant Systems).", PassiveHook.AmbushDodgeChancePct, 0.15),
        new(CharacterClass.Engineer, 46, "Scrap Efficiency", "+10% value converting Junk items (stacks with Salvage Sense).", PassiveHook.JunkValueBonusPct, 0.10),
        new(CharacterClass.Engineer, 51, "Reserve Capacitor", "An extra 15% Tachyon-cost reduction under Failsafe Capacitor's low-pool condition.", PassiveHook.LowTachyonCastDiscountPct, 0.15),
        new(CharacterClass.Engineer, 56, "Universal Toolkit", "Off-class weapon penalty eliminated entirely (stacks with Field-Tested Gear).", PassiveHook.OffClassPenaltyReductionPct, 0.5),
    ];

    /// <summary>All passives <paramref name="cls"/> has unlocked by <paramref name="level"/> (inclusive).</summary>
    public static IEnumerable<PassiveTrait> Unlocked(CharacterClass cls, int level) =>
        All.Where(p => p.Class == cls && p.Level <= level);

    /// <summary>
    /// Sums the magnitude of every unlocked passive using <paramref name="hook"/> —
    /// the normal way to read a hook, since a couple (Spy's store discount)
    /// are deliberately split across two levels that stack.
    /// </summary>
    public static double Sum(CharacterClass cls, int level, PassiveHook hook) =>
        Unlocked(cls, level).Where(p => p.Hook == hook).Sum(p => p.Magnitude);

    /// <summary>True if <paramref name="cls"/> has unlocked any passive using <paramref name="hook"/> by <paramref name="level"/>.</summary>
    public static bool Any(CharacterClass cls, int level, PassiveHook hook) =>
        Unlocked(cls, level).Any(p => p.Hook == hook);
}
