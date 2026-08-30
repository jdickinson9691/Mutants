using Mutants.Core.Levels;
using Mutants.Core.Monsters;
using Mutants.Core.World;

namespace Mutants.Core.Tests.Levels;

public class GameWorldTests
{
    private static WorldLevelDefinition Level(int number, Func<Monster>? gatekeeper = null) =>
        new(number, TestLevel.Build(), TestMonsters.RosterFor(number), [], gatekeeper, minCharacterLevelToUnlock: 1);

    [Fact]
    public void Constructor_RejectsEmptyLevelList()
    {
        Assert.Throws<ArgumentException>(() => new GameWorld([]));
    }

    [Fact]
    public void Constructor_RejectsNonContiguousLevelNumbers()
    {
        var levels = new[] { Level(1), Level(3) }; // missing level 2
        Assert.Throws<ArgumentException>(() => new GameWorld(levels));
    }

    [Fact]
    public void Constructor_RejectsLevelOneHavingAGatekeeper()
    {
        var levels = new[] { Level(1, gatekeeper: () => TestMonsters.Gatekeeper(1)) };
        Assert.Throws<ArgumentException>(() => new GameWorld(levels));
    }

    [Fact]
    public void Constructor_AcceptsLevelsSuppliedOutOfOrder()
    {
        var world = new GameWorld([Level(2, gatekeeper: () => TestMonsters.Gatekeeper(2)), Level(1)]);
        Assert.Equal(2, world.MaxLevel);
    }

    [Fact]
    public void TryGetLevel_ReturnsNullForAnUndefinedLevel()
    {
        var world = new GameWorld([Level(1)]);
        Assert.Null(world.TryGetLevel(5));
    }

    [Fact]
    public void GetLevel_ThrowsForAnUndefinedLevel()
    {
        var world = new GameWorld([Level(1)]);
        Assert.Throws<KeyNotFoundException>(() => world.GetLevel(5));
    }

    [Fact]
    public void GetLevel_ReturnsTheMatchingDefinition()
    {
        var level1 = Level(1);
        var world = new GameWorld([level1]);

        Assert.Same(level1, world.GetLevel(1));
    }
}
