namespace ChronoTravelers.Engine.Combat;

/// <summary>What an ability mechanically does — see ChronoTravelers.Content's abilities.json "effect" field and CombatSession.Cast.</summary>
public enum AbilityEffectType
{
    /// <summary>No combat effect — a passive, economy, overworld, or party mechanic this engine doesn't have yet.</summary>
    None,

    /// <summary>An enhanced attack this round: Magnitude is a multiplier on the caster's normal attack damage.</summary>
    Damage,

    /// <summary>Like Damage, but the hit ignores the target's Defense entirely.</summary>
    IgnoreDefenseDamage,

    /// <summary>Instantly heals the caster. Magnitude is a fraction of max HP.</summary>
    Heal,

    /// <summary>Flat bonus to the caster's attack for the rest of the fight.</summary>
    BuffSelfAttack,

    /// <summary>Flat bonus to the caster's defense for the rest of the fight.</summary>
    BuffSelfDefense,

    /// <summary>Flat reduction to the monster's attack for the rest of the fight.</summary>
    DebuffTargetAttack,

    /// <summary>Flat reduction to the monster's defense for the rest of the fight.</summary>
    DebuffTargetDefense,

    /// <summary>Flat reduction to the monster's speed for the rest of the fight.</summary>
    DebuffTargetSpeed,

    /// <summary>The caster's next normal attack (this round or a future one) deals bonus damage.</summary>
    GuaranteedCritNextAttack,

    /// <summary>The caster immediately makes a second attack this round.</summary>
    ExtraAttack,

    /// <summary>Absorbs the next incoming monster attack entirely (one charge).</summary>
    Shield,

    /// <summary>An enhanced attack this round that also ticks for Magnitude damage at the end of each of the next DurationRounds.</summary>
    DamageOverTime,

    /// <summary>Instantly restores a fraction (Magnitude) of the caster's max Tachyons.</summary>
    RestoreTachyons,

    /// <summary>Instantly defeats the monster outright — refused against a warden/boss fight.</summary>
    InstantDefeatNonBoss,

    /// <summary>
    /// Overworld-only, not a combat effect — refused mid-fight, cast instead
    /// via the out-of-combat <c>cast &lt;ability&gt;</c> command (see
    /// ChronoTravelers.Engine.Combat.OverworldAbilityResolver). Teleports
    /// the caster to a random room within Magnitude exit-hops of their
    /// current position (docs/GDD.md's Engineer "Jump Rig": "a rigged
    /// micro-jump — short teleport to escape or reposition").
    /// </summary>
    ShortTeleport,

    /// <summary>
    /// Overworld-only, not a combat effect — refused mid-fight, cast instead
    /// via the out-of-combat <c>cast &lt;ability&gt;</c> command (see
    /// ChronoTravelers.Engine.Combat.OverworldAbilityResolver). Heals the
    /// most wounded living NPC Traveler sharing the caster's room (a
    /// genuinely 0-HP NPC is replaced by the next tick's respawn far too
    /// quickly to ever be a usable target — see OverworldAbilityResolver's
    /// doc comment for why "downed" is read as "badly wounded" instead) up
    /// to Magnitude of their max HP, if they're not already above it
    /// (docs/GDD.md's Doctor "Crash Cart": "bring a downed ally back on
    /// partial vitals").
    /// </summary>
    ReviveAlly,
}
