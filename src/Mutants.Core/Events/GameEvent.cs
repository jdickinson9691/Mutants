namespace Mutants.Core.Events;

/// <summary>
/// One entry on the shared "telepathic broadcast" / kill-feed channel —
/// docs/GDD.md §7: NPCs and the player post to the same channel ("X was
/// slain by Y," "Z reached level N," "W time traveled to level 5") so the
/// world feels alive without a live human population.
/// </summary>
public sealed record GameEvent(string Message)
{
    public static GameEvent Slain(string victimName, string killerName) =>
        new($"{victimName} was slain by {killerName}.");

    public static GameEvent LevelReached(string name, int level) =>
        new($"{name} reached level {level}!");

    public static GameEvent TimeTraveled(string name, int level) =>
        new($"{name} time traveled to level {level}.");
}
