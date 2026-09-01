using ChronoTravelers.Core.Time;
using ChronoTravelers.Engine.Npc;

namespace ChronoTravelers.Engine.Tests.Npc;

public class NpcPopulationTests
{
    private static TimeWorld World() => TestTimeWorld.Build(seed: 5150);

    [Fact]
    public void Spawn_ProducesRequestedCount()
    {
        var npcs = NpcPopulation.Spawn(15, World(), StubRandomSource.Fixed(0.5));
        Assert.Equal(15, npcs.Count);
    }

    [Fact]
    public void Spawn_PlacesEveryNpcAtItsStartYearsStartRoom()
    {
        var world = World();
        var npcs = NpcPopulation.Spawn(10, world, new StubRandomSource(0.0, 0.25, 0.5, 0.75, 0.99));

        Assert.All(npcs, npc => Assert.Equal(world.GetYear(npc.CurrentYear).Map.Start, npc.Position));
    }

    [Fact]
    public void Spawn_ScattersNpcsAcrossTheTimeline()
    {
        var npcs = NpcPopulation.Spawn(30, World(), new StubRandomSource(0.05, 0.35, 0.65, 0.95, 0.15, 0.55, 0.85));

        Assert.All(npcs, npc => Assert.InRange(npc.CurrentYear, TimeScale.MinYear, TimeScale.MaxYear));
        Assert.True(npcs.Select(n => n.CurrentYear).Distinct().Count() > 1, "NPCs should not all start in the same year.");
    }

    [Fact]
    public void Spawn_GivesEveryNpcAUniqueNonEmptyName()
    {
        var npcs = NpcPopulation.Spawn(25, World(), StubRandomSource.Fixed(0.5)); // more than the base name pool

        Assert.All(npcs, npc => Assert.False(string.IsNullOrWhiteSpace(npc.Name)));
        Assert.Equal(npcs.Count, npcs.Select(n => n.Name).Distinct().Count());
    }

    [Fact]
    public void Spawn_TopsUpEveryNpcToFullHealthAndTachyons()
    {
        var npcs = NpcPopulation.Spawn(8, World(), new StubRandomSource(0.1, 0.5, 0.9, 0.3, 0.7));

        Assert.All(npcs, npc =>
        {
            Assert.Equal(npc.Health.Max, npc.Health.Current);
            Assert.Equal(npc.Tachyons.Max, npc.Tachyons.Current);
        });
    }

    [Fact]
    public void Spawn_LevelsNpcsIntoTheirStartYearsSoftCapBand()
    {
        var npcs = NpcPopulation.Spawn(12, World(), new StubRandomSource(0.2, 0.6, 0.4, 0.8));

        Assert.All(npcs, npc =>
        {
            var cap = TimeScale.SoftLevelCapForYear(npc.CurrentYear);
            Assert.InRange(npc.Level, 1, cap);
        });
    }

    [Fact]
    public void Spawn_RejectsNegativeCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NpcPopulation.Spawn(-1, World(), StubRandomSource.Fixed(0.5)));
    }

    [Fact]
    public void Spawn_ZeroCountProducesEmptyList()
    {
        Assert.Empty(NpcPopulation.Spawn(0, World(), StubRandomSource.Fixed(0.5)));
    }
}
