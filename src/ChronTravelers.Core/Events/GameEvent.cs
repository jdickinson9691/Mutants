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
/// the world feels alive without a live human population. <see cref="Year"/>
/// is the timeline year the event happened in (null if it isn't tied to
/// one), so the console can show only what's happening in the player's own
/// year inline and leave the rest for <c>news</c>.
/// </summary>
public sealed record GameEvent(string Message, GameEventKind Kind = GameEventKind.Slain, int? Year = null)
{
    public static GameEvent Slain(string victimName, string killerName, int? year = null) =>
        new($"{victimName} was slain by {killerName}.", GameEventKind.Slain, year);

    public static GameEvent LevelReached(string name, int level, int? year = null) =>
        new($"{name} reached level {level}!", GameEventKind.LevelReached, year);

    public static GameEvent TimeTraveled(string name, int year) =>
        new($"{name} time traveled to {year} A.D.", GameEventKind.TimeTraveled, year);

    public static GameEvent Ambushed(string monsterName, string victimName, int damage, int? year = null) =>
        new($"{monsterName} ambushes {victimName} for {damage}.", GameEventKind.Ambushed, year);
}
