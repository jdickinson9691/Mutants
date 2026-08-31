namespace ChronTravelers.Core.Items;

/// <summary>
/// The optional non-damage effect a ranged shot applies on top of its
/// damage — small on purpose (Wands "cast an effect or do damage"); the
/// full ability taxonomy stays in ChronTravelers.Engine.Combat.AbilityEffectType.
/// </summary>
public enum RangedEffectType
{
    /// <summary>Damage only.</summary>
    None,

    /// <summary>
    /// The target carries a defence penalty into its <em>next</em> fight
    /// (<see cref="ChronTravelers.Core.Monsters.Monster.PendingDefensePenalty"/>),
    /// consumed once by that ChronTravelers.Engine.Combat.CombatSession.
    /// </summary>
    Weaken,
}
