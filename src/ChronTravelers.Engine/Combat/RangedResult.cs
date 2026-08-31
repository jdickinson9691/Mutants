namespace ChronTravelers.Engine.Combat;

/// <summary>Outcome of a single <see cref="RangedResolver.Fire"/> shot.</summary>
public sealed record RangedResult(int Damage, bool Killed, string Message);
