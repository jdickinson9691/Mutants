using Mutants.Core.Levels;

namespace Mutants.Core.Tests.Levels;

public class TestWorldTests
{
    [Fact]
    public void Build_ProducesThreeLevels()
    {
        Assert.Equal(3, TestWorld.Build().MaxLevel);
    }

    [Fact]
    public void Build_LevelOneHasNoGatekeeperAndIsReachableFromTheStart()
    {
        var level1 = TestWorld.Build().GetLevel(1);
        Assert.Null(level1.Gatekeeper);
        Assert.Equal(1, level1.MinCharacterLevelToUnlock);
    }

    [Fact]
    public void Build_DeeperLevelsHaveGatekeepersAndRisingMinimumCharacterLevels()
    {
        var world = TestWorld.Build();
        var level2 = world.GetLevel(2);
        var level3 = world.GetLevel(3);

        Assert.NotNull(level2.Gatekeeper);
        Assert.NotNull(level3.Gatekeeper);
        Assert.True(level3.MinCharacterLevelToUnlock > level2.MinCharacterLevelToUnlock);
    }

    [Fact]
    public void Build_EveryLevelsMapIsWellFormed()
    {
        var world = TestWorld.Build();
        for (var i = 1; i <= world.MaxLevel; i++)
        {
            Assert.Empty(world.GetLevel(i).Map.Validate());
        }
    }

    [Fact]
    public void Build_EveryLevelsMonsterRosterMatchesTheLevelsTier()
    {
        var world = TestWorld.Build();
        for (var i = 1; i <= world.MaxLevel; i++)
        {
            var level = world.GetLevel(i);
            Assert.All(level.MonsterRoster, factory => Assert.Equal(i, factory().Tier));
        }
    }

    [Fact]
    public void Build_GatekeepersMatchTheirLevelsTier()
    {
        var world = TestWorld.Build();
        Assert.Equal(2, world.GetLevel(2).Gatekeeper!().Tier);
        Assert.Equal(3, world.GetLevel(3).Gatekeeper!().Tier);
    }

    [Fact]
    public void Build_Level2StoreSitsInARoomThatExistsOnLevel2sMap()
    {
        var world = TestWorld.Build();
        var level2 = world.GetLevel(2);

        Assert.Single(level2.StoreSlots);
        Assert.NotNull(level2.Map.TryGetRoom(level2.StoreSlots[0].Location));
    }
}
