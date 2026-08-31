using ChronTravelers.Core.Time;

namespace ChronTravelers.Engine.Persistence;

/// <summary>
/// One character's all-time personal bests — docs/GDD.md §8: "Furthest
/// Year Reached" and "Highest Character Level," "across player + NPCs,"
/// persisted "so a leaderboard has meaning across NPC-simulated 'seasons'
/// of play, not just the current process's runtime." One row per distinct
/// character name; values only ever climb (see
/// GameRepository.RecordPersonalBests).
/// </summary>
public sealed class LeaderboardEntry
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsPlayer { get; set; }

    /// <summary>The furthest-future year this character has ever reached (2000–5000). Replaces the old "deepest time level".</summary>
    public int FurthestYearReached { get; set; } = TimeScale.MinYear;

    public int HighestCharacterLevelReached { get; set; } = 1;
    public DateTime LastUpdatedUtc { get; set; }
}
