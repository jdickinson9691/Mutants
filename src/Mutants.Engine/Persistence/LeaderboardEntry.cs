namespace Mutants.Engine.Persistence;

/// <summary>
/// One character's all-time personal bests — docs/GDD.md §8: "Deepest
/// Time Travel Level Reached" and "Highest Character Level," "across
/// player + NPCs," persisted "so a leaderboard has meaning across
/// NPC-simulated 'seasons' of play, not just the current process's
/// runtime." One row per distinct character name; values only ever climb
/// (see GameRepository.RecordPersonalBests).
/// </summary>
public sealed class LeaderboardEntry
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsPlayer { get; set; }
    public int DeepestTimeLevelReached { get; set; } = 1;
    public int HighestCharacterLevelReached { get; set; } = 1;
    public DateTime LastUpdatedUtc { get; set; }
}
