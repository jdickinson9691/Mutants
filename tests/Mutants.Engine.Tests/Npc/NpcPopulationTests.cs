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
}
