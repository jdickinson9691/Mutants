using Mutants.Engine.Content;

namespace Mutants.Engine.Tests.Content;

/// <summary>
/// Loads the actual shipped content in src/Mutants.Content — a
/// regression test that the real data stays internally consistent (every
/// cross-reference resolves, every level's map is well-formed) as it's
/// edited over time, not just that the loader mechanics work on
/// hand-crafted samples (see ContentLoaderTests).
///
/// Note: these tests deliberately avoid xUnit's Assert.Contains(collection,
/// predicate) overload and Assert.Equal(IEnumerable, IEnumerable) —
/// something in the test-runner setup on this machine chokes on those
/// specific overloads (a "could not find dependent assembly" failure at
/// test-discovery time, before any test even runs), even though plain
/// LINQ (.Any(), .Select(), etc.) and Assert.All work fine. HashSet/manual
/// checks below are a deliberate workaround, not a style preference.
/// </summary>
public class RealContentTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Mutants.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException(
            $"Could not locate repo root (Mutants.sln) walking up from {AppContext.BaseDirectory}.");
    }

    private static string RealContentDirectory() => Path.Combine(RepoRoot(), "src", "Mutants.Content");

    [Fact]
    public void ShippedContent_LoadsWithoutError()
    {
        var world = ContentLoader.LoadWorld(RealContentDirectory());
        Assert.True(world.MaxLevel >= 1);
    }

    [Fact]
    public void ShippedContent_EveryLevelsMapIsWellFormed()
    {
        var world = ContentLoader.LoadWorld(RealContentDirectory());
        for (var i = 1; i <= world.MaxLevel; i++)
        {
            var problems = world.GetLevel(i).Map.Validate();
            Assert.True(problems.Count == 0, $"Level {i}: {string.Join("; ", problems)}");
        }
    }

    [Fact]
    public void ShippedContent_EveryLevelsMapIsFullyConnectedFromItsStartRoom()
    {
        var world = ContentLoader.LoadWorld(RealContentDirectory());
        var levelFiles = Directory.GetFiles(Path.Combine(RealContentDirectory(), "levels"), "*.json");
        var jsonOptions = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        for (var levelNumber = 1; levelNumber <= world.MaxLevel; levelNumber++)
        {
            var map = world.GetLevel(levelNumber).Map;
            var visited = new HashSet<Core.World.Coordinate> { map.Start };
            var frontier = new Queue<Core.World.Coordinate>();
            frontier.Enqueue(map.Start);

            while (frontier.Count > 0)
            {
                var current = frontier.Dequeue();
                var room = map.GetRoom(current);
                foreach (var direction in room.ExitDescriptions.Keys)
                {
                    var next = current.Move(direction);
                    if (visited.Add(next))
                    {
                        frontier.Enqueue(next);
                    }
                }
            }

            LevelData? matchingLevel = null;
            foreach (var file in levelFiles)
            {
                var candidate = System.Text.Json.JsonSerializer.Deserialize<LevelData>(File.ReadAllText(file), jsonOptions);
                if (candidate is not null && candidate.LevelNumber == levelNumber)
                {
                    matchingLevel = candidate;
                    break;
                }
            }

            Assert.NotNull(matchingLevel);
            Assert.Equal(matchingLevel!.Rooms.Count, visited.Count);
        }
    }

    [Fact]
    public void ShippedContent_GatekeepersAreTierAppropriate()
    {
        var world = ContentLoader.LoadWorld(RealContentDirectory());

        for (var levelNumber = 2; levelNumber <= world.MaxLevel; levelNumber++)
        {
            var level = world.GetLevel(levelNumber);
            Assert.NotNull(level.Gatekeeper);
            Assert.Equal(levelNumber, level.Gatekeeper!().Tier);
        }
    }

    [Fact]
    public void ShippedContent_LevelOneHasNoGatekeeper()
    {
        var world = ContentLoader.LoadWorld(RealContentDirectory());
        Assert.Null(world.GetLevel(1).Gatekeeper);
    }

    [Fact]
    public void ShippedContent_EveryMonsterRosterEntryMatchesItsLevelsTier()
    {
        var world = ContentLoader.LoadWorld(RealContentDirectory());

        for (var levelNumber = 1; levelNumber <= world.MaxLevel; levelNumber++)
        {
            var level = world.GetLevel(levelNumber);
            Assert.NotEmpty(level.MonsterRoster);
            Assert.All(level.MonsterRoster, factory => Assert.Equal(levelNumber, factory().Tier));
        }
    }

    [Fact]
    public void ShippedAbilities_HaveExactlySixTiersForEachOfTheFiveClasses()
    {
        var abilities = ContentLoader.LoadAbilities(Path.Combine(RealContentDirectory(), "abilities.json"));
        var byClass = abilities.GroupBy(a => a.Class).ToDictionary(g => g.Key, g => g.ToList());

        Assert.Equal(5, byClass.Count);

        foreach (var pair in byClass)
        {
            var entries = pair.Value;
            Assert.Equal(6, entries.Count);

            var tiersPresent = new HashSet<int>();
            var levelsPresent = new HashSet<int>();
            foreach (var entry in entries)
            {
                tiersPresent.Add(entry.Tier);
                levelsPresent.Add(entry.Level);
            }

            Assert.Equal(6, tiersPresent.Count);
            for (var tier = 1; tier <= 6; tier++)
            {
                Assert.True(tiersPresent.Contains(tier), $"{pair.Key} is missing tier {tier}.");
            }

            var expectedLevels = new[] { 5, 10, 15, 20, 25, 30 };
            Assert.Equal(6, levelsPresent.Count);
            foreach (var level in expectedLevels)
            {
                Assert.True(levelsPresent.Contains(level), $"{pair.Key} is missing the level-{level} ability.");
            }

            Assert.True(Enum.TryParse<Core.Classes.CharacterClass>(pair.Key, out _), $"'{pair.Key}' isn't a real class.");
        }
    }

    [Fact]
    public void ShippedNpcPopulation_HasAnEntryForEveryLevel()
    {
        var world = ContentLoader.LoadWorld(RealContentDirectory());
        var config = ContentLoader.LoadNpcPopulation(Path.Combine(RealContentDirectory(), "npc-population.json"));

        var levelsCovered = new HashSet<int>();
        foreach (var entry in config)
        {
            levelsCovered.Add(entry.LevelNumber);
        }

        for (var levelNumber = 1; levelNumber <= world.MaxLevel; levelNumber++)
        {
            Assert.True(levelsCovered.Contains(levelNumber), $"No NPC population entry for level {levelNumber}.");
        }
    }
}
