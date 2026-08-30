using Mutants.Core.Levels;
using Mutants.Core.Monsters;
using Mutants.Core.World;

namespace Mutants.Core.Tests.Levels;

public class WorldLevelDefinitionTests
{
    private static WorldLevelDefinition BuildLevel(int levelNumber = 1, int minCharacterLevelToUnlock = 1, Func<Monster>? gatekeeper = null) =>
        new(levelNumber, TestLevel.Build(), TestMonsters.RosterFor(levelNumber), [], gatekeeper, minCharacterLevelToUnlock);

    [Fact]
    public void Constructor_RejectsLevelNumberBelowOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildLevel(levelNumber: 0));
    }

    [Fact]
    public void Constructor_RejectsMinCharacterLevelBelowOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildLevel(minCharacterLevelToUnlock: 0));
    }

    [Fact]
    public void Constructor_DefaultsToNoGatekeeperAndMinLevelOne()
    {
        var level = BuildLevel();
        Assert.Null(level.Gatekeeper);
        Assert.Equal(1, level.MinCharacterLevelToUnlock);
    }
}
