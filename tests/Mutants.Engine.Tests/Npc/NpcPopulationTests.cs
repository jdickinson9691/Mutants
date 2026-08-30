using Mutants.Core.World;
using Mutants.Engine.Npc;

namespace Mutants.Engine.Tests.Npc;

public class NpcPopulationTests
{
    [Fact]
    public void Spawn_ProducesRequestedCount()
    {
        var level = TestLevel.Build();
        var npcs = NpcPopulation.Spawn(5, level, StubRandomSource.Fixed(0.5));

        Assert.Equal(5, npcs.Count);
    }

    [Fact]
    public void Spawn_PlacesEveryNpcAtLevelStart()
    {
        var level = TestLevel.Build();
        var npcs = NpcPopulation.Spawn(3, level, StubRandomSource.Fixed(0.5));

        Assert.All(npcs, npc => Assert.Equal(level.Start, npc.Position));
    }

    [Fact]
    public void Spawn_GivesEveryNpcAUniqueNonEmptyName()
    {
        var level = TestLevel.Build();
        var npcs = NpcPopulation.Spawn(12, level, StubRandomSource.Fixed(0.5)); // more than the base name pool

        Assert.All(npcs, npc => Assert.False(string.IsNullOrWhiteSpace(npc.Name)));
        Assert.Equal(npcs.Count, npcs.Select(n => n.Name).Distinct().Count());
    }

    [Fact]
    public void Spawn_RejectsNegativeCount()
    {
        var level = TestLevel.Build();
        Assert.Throws<ArgumentOutOfRangeException>(() => NpcPopulation.Spawn(-1, level, StubRandomSource.Fixed(0.5)));
    }

    [Fact]
    public void Spawn_ZeroCountProducesEmptyList()
    {
        var level = TestLevel.Build();
        Assert.Empty(NpcPopulation.Spawn(0, level, StubRandomSource.Fixed(0.5)));
    }

    [Fact]
    public void Spawn_AtADeeperHomeLevel_UnlocksThroughItAndPlacesAtItsStart()
    {
        var level = TestLevel.Build();
        var npcs = NpcPopulation.Spawn(2, level, StubRandomSource.Fixed(0.5), homeLevelNumber: 3, minCharacterLevel: 1, maxCharacterLevel: 1);

        Assert.All(npcs, npc =>
        {
            Assert.Equal(3, npc.UnlockedTimeLevel);
            Assert.Equal(3, npc.CurrentTimeLevel);
            Assert.Equal(level.Start, npc.Position);
        });
    }

    [Fact]
    public void Spawn_AtADeeperHomeLevel_NamesDoNotCollideWithLevelOnesDefaultNames()
    {
        var level = TestLevel.Build();
        var level1Npcs = NpcPopulation.Spawn(3, level, StubRandomSource.Fixed(0.5));
        var level2Npcs = NpcPopulation.Spawn(3, level, StubRandomSource.Fixed(0.5), homeLevelNumber: 2);

        var allNames = level1Npcs.Concat(level2Npcs).Select(n => n.Name).ToList();
        Assert.Equal(allNames.Count, allNames.Distinct().Count());
    }

    [Fact]
    public void Spawn_WithACharacterLevelRange_StartsSomewhereWithinItAndAtFullHealthAndIons()
    {
        var level = TestLevel.Build();
        var npcs = NpcPopulation.Spawn(5, level, new StubRandomSource(0.99, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5), homeLevelNumber: 2, minCharacterLevel: 4, maxCharacterLevel: 8);

        Assert.All(npcs, npc =>
        {
            Assert.InRange(npc.Level, 4, 8);
            Assert.Equal(npc.Health.Max, npc.Health.Current);
            Assert.Equal(npc.Ions.Max, npc.Ions.Current);
        });
    }

    [Fact]
    public void Spawn_DefaultCharacterLevelRange_MatchesTheOriginalAlwaysLevelOneBehavior()
    {
        var level = TestLevel.Build();
        var npcs = NpcPopulation.Spawn(4, level, StubRandomSource.Fixed(0.5));

        Assert.All(npcs, npc => Assert.Equal(1, npc.Level));
    }
}
