namespace ChronoTravelers.Engine.Combat;

/// <summary>Outcome of one <see cref="CombatSession.Cast"/> attempt.</summary>
public sealed record AbilityCastResult(bool Success, string Message);
