namespace ChronoTravelers.Engine.Combat;

/// <summary>Outcome of <see cref="CombatSession.UseItem"/> — mirrors <see cref="AbilityCastResult"/>'s shape.</summary>
public sealed record ItemUseResult(bool Success, string Message);
