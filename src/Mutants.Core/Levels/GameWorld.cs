namespace Mutants.Core.Levels;

/// <summary>
/// The full sequence of time-travel levels — docs/GDD.md §3.2: "the world
/// is organized into a sequence of time-travel levels ... Level 1 is the
/// 'present' city; each level deeper is a different era." Levels must be
/// numbered contiguously starting at 1 (no gaps), matching "levels only
/// go one direction deeper until a level cap."
/// </summary>
public sealed class GameWorld
{
    public int MaxLevel { get; }

    private readonly Dictionary<int, WorldLevelDefinition> _levels;

    public GameWorld(IReadOnlyList<WorldLevelDefinition> levels)
    {
        if (levels.Count == 0)
        {
            throw new ArgumentException("A world must contain at least one level.", nameof(levels));
        }

        var ordered = levels.OrderBy(l => l.LevelNumber).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var expected = i + 1;
            if (ordered[i].LevelNumber != expected)
            {
                throw new ArgumentException(
                    $"Levels must be numbered contiguously starting at 1 (expected level {expected}, found {ordered[i].LevelNumber}).",
                    nameof(levels));
            }
        }

        if (ordered[0].Gatekeeper is not null)
        {
            throw new ArgumentException("Level 1 must not require a gatekeeper — it needs no unlock.", nameof(levels));
        }

        _levels = ordered.ToDictionary(l => l.LevelNumber);
        MaxLevel = ordered.Count;
    }

    public WorldLevelDefinition? TryGetLevel(int levelNumber) => _levels.GetValueOrDefault(levelNumber);

    public WorldLevelDefinition GetLevel(int levelNumber) =>
        TryGetLevel(levelNumber) ?? throw new KeyNotFoundException($"No level numbered {levelNumber}.");
}
