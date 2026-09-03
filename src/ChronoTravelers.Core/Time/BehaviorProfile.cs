namespace ChronoTravelers.Core.Time;

/// <summary>
/// Per-species behavior traits layered on top of
/// <c>ChronoTravelers.Engine.Npc.MonsterController</c>'s shared tick loop —
/// the same wander/aggro/infight/heal loop every monster has always run,
/// now with room for a species to lean away from it. Every field defaults
/// to "off" (today's one-size-fits-all behavior); see
/// <see cref="SpeciesDefinition.EffectiveBehaviorProfile"/> for the one
/// exception (an archetype-based flee default) applied when a species
/// authors no profile at all.
/// </summary>
public sealed record BehaviorProfile(
    /// <summary>
    /// Fraction of max HP at/below which this species breaks off and runs
    /// from the player (<c>Monster.IsFleeing</c>, stepping toward whichever
    /// exit most increases distance) instead of standing its ground or
    /// pursuing — see <c>MonsterController</c>'s ranged-hit reaction. 0
    /// means it never flees.
    /// </summary>
    double FleeBelowHpFraction = 0.0,

    /// <summary>When one of this species is aggro'd, every other living monster of the same species sharing its room is raised to the same aggro — a nest wakes together instead of one at a time.</summary>
    bool PackHunting = false,

    /// <summary>Excluded from <c>MonsterController.ResolveInfighting</c>'s pairing — never turns on its own kind (or anyone else) when a room gets crowded.</summary>
    bool NeverInfights = false,

    /// <summary>Added to <c>MonsterController.AggroRange</c> for this species — how many extra rooms out it notices and tracks the player.</summary>
    int AggroRangeBonus = 0,

    /// <summary>Scales the damage of this species' ambush hit once Hostile — 1.0 = unchanged. See <c>MonsterController.ResolveAmbush</c>.</summary>
    double AmbushDamageMultiplier = 1.0)
{
    /// <summary>Every trait off — today's shared behavior, unmodified.</summary>
    public static readonly BehaviorProfile Default = new();
}
