namespace ChronoTravelers.Core.Time;

/// <summary>
/// Per-species stat multipliers layered on top of a species' archetype-
/// shifted tier baseline (see <see cref="MonsterArchetype"/> and
/// <c>TimelineContentFactory.StatsFor</c>) — the monster-roster
/// counterpart to an item archetype's <c>powerMultiplier</c>
/// (<see cref="ItemArchetypeDefinition.PowerMultiplier"/>). Without this,
/// every species sharing an archetype lands on numerically identical
/// stats for a given tier — two Bruisers anywhere in the game are the
/// same monster wearing a different name. All multipliers default to
/// 1.0 (no change from the archetype-shifted baseline), so a species that
/// doesn't author one behaves exactly as it did before this existed.
/// </summary>
public sealed record PowerProfile(
    double HpMultiplier = 1.0,
    double AttackMultiplier = 1.0,
    double DefenseMultiplier = 1.0,
    double SpeedMultiplier = 1.0)
{
    /// <summary>Every multiplier at 1.0 — the archetype-shifted baseline, unmodified.</summary>
    public static readonly PowerProfile Default = new();
}
