using LiteDB;

namespace Mutants.Engine.Persistence;

/// <summary>
/// The local save file / leaderboard database — docs/TECH_STACK.md
/// recommends LiteDB for exactly this ("a local single-player save file
/// that also has to store leaderboard history"). AGENTS.md assigns
/// "persistence layer" to the Systems/Engine Agent. A real packaged build
/// would put this file under a platform-appropriate app-data directory
/// (that's packaging work, milestone 8); the caller decides the path.
/// </summary>
public sealed class GameRepository : IDisposable
{
    private const string CharactersCollection = "characters";
    private const string LeaderboardCollection = "leaderboard";

    private readonly LiteDatabase _db;

    public GameRepository(string path)
    {
        _db = new LiteDatabase(path);
    }

    /// <summary>An in-memory database — for tests, or a "don't persist anything" run.</summary>
    public static GameRepository InMemory() => new(new LiteDatabase(new MemoryStream()));

    private GameRepository(LiteDatabase db)
    {
        _db = db;
    }

    /// <summary>Saves (or overwrites, by name) the player's character.</summary>
    public void SaveCharacter(CharacterSaveData data)
    {
        var collection = _db.GetCollection<CharacterSaveData>(CharactersCollection);
        collection.EnsureIndex(c => c.Name, unique: true);

        var existing = collection.FindOne(c => c.Name == data.Name);
        if (existing is not null)
        {
            data.Id = existing.Id;
        }

        collection.Upsert(data);
    }

    public CharacterSaveData? LoadCharacter(string name) =>
        _db.GetCollection<CharacterSaveData>(CharactersCollection).FindOne(c => c.Name == name);

    public IReadOnlyList<string> ListSavedCharacterNames() =>
        _db.GetCollection<CharacterSaveData>(CharactersCollection)
            .FindAll()
            .Select(c => c.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Records a character's current stats as personal bests if they beat
    /// what's on file — docs/GDD.md §8: the two leaderboards only ever
    /// track each character's best-ever showing, never a snapshot that
    /// could regress.
    /// </summary>
    public void RecordPersonalBests(string name, bool isPlayer, int furthestYearReached, int highestCharacterLevel)
    {
        var collection = _db.GetCollection<LeaderboardEntry>(LeaderboardCollection);
        collection.EnsureIndex(e => e.Name, unique: true);

        var entry = collection.FindOne(e => e.Name == name) ?? new LeaderboardEntry { Name = name, IsPlayer = isPlayer };

        entry.IsPlayer = isPlayer;
        entry.FurthestYearReached = Math.Max(entry.FurthestYearReached, furthestYearReached);
        entry.HighestCharacterLevelReached = Math.Max(entry.HighestCharacterLevelReached, highestCharacterLevel);
        entry.LastUpdatedUtc = DateTime.UtcNow;

        collection.Upsert(entry);
    }

    /// <summary>Top entries by furthest year reached, highest first.</summary>
    public IReadOnlyList<LeaderboardEntry> TopByFurthestYear(int count) =>
        _db.GetCollection<LeaderboardEntry>(LeaderboardCollection)
            .FindAll()
            .OrderByDescending(e => e.FurthestYearReached)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Take(count)
            .ToList();

    /// <summary>Top entries by highest character level reached, highest first.</summary>
    public IReadOnlyList<LeaderboardEntry> TopByCharacterLevel(int count) =>
        _db.GetCollection<LeaderboardEntry>(LeaderboardCollection)
            .FindAll()
            .OrderByDescending(e => e.HighestCharacterLevelReached)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Take(count)
            .ToList();

    public LeaderboardEntry? GetLeaderboardEntry(string name) =>
        _db.GetCollection<LeaderboardEntry>(LeaderboardCollection).FindOne(e => e.Name == name);

    public void Dispose() => _db.Dispose();
}
