namespace ChronoTravelers.Core.Items;

/// <summary>
/// The optional non-damage effect a ranged shot applies on top of its
/// damage — small on purpose (Wands "cast an effect or do damage"); the
/// full ability taxonomy stays in ChronoTravelers.Engine.Combat.AbilityEffectType.
/// </summary>
public enum RangedEffectType
{
    /// <summary>Damage only.</summary>
    None,

    /// <summary>
    /// The target carries a defence penalty into its <em>next</em> fight
    /// (<see cref="ChronoTravelers.Core.Monsters.Monster.PendingDefensePenalty"/>),
    /// consumed once by that ChronoTravelers.Engine.Combat.CombatSession.
    /// </summary>
    Weaken,

    /// <summary>
    /// The target carries an attack penalty into its <em>next</em> fight
    /// (<see cref="ChronoTravelers.Core.Monsters.Monster.PendingAttackPenalty"/>),
    /// consumed once by that ChronoTravelers.Engine.Combat.CombatSession —
    /// the offense-side counterpart to <see cref="Weaken"/> (which hits
    /// defense instead), e.g. a stunning/taser-style shot.
    /// </summary>
    Stagger,
}
