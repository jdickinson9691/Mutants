namespace ChronTravelers.Core.Events;

/// <summary>What a <see cref="GameEvent"/> reports — lets the UI treat the noisy background chatter (NPC time-hops) differently from events that matter to the player right now (kills, level-ups, ambushes).</summary>
public enum GameEventKind
{
    Slain,
    LevelReached,
    TimeTraveled,
    Ambushed,
}

/// <summary>
/// One entry on the shared "telepathic broadcast" / kill-feed channel —
/// docs/GDD.md §7: NPCs and the player post to the same channel ("X was
/// slain by Y," "Z reached level N," "W time traveled to 3200 A.D.") so
/// the world feels alive without a live human population.
/// </summary>
public sealed record GameEvent(string Message, GameEventKind Kind = GameEventKind.Slain)
{
    public static GameEvent Slain(string victimName, string killerName) =>
        new($"{victimName} was slain by {killerName}.", GameEventKind.Slain);

    public static GameEvent LevelReached(string name, int level) =>
        new($"{name} reached level {level}!", GameEventKind.LevelReached);

    public static GameEvent TimeTraveled(string name, int year) =>
        new($"{name} time traveled to {year} A.D.", GameEventKind.TimeTraveled);

    public static GameEvent Ambushed(string monsterName, string victimName, int damage) =>
        new($"{monsterName} ambushes {victimName} for {damage}.", GameEventKind.Ambushed);
}
