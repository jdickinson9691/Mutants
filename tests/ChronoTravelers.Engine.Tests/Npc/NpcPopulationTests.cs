using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Time;
using ChronoTravelers.Core.Traits;
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

    [Fact]
    public void RespawnNear_LandsWithinTheSpreadOfTheAnchorYear()
    {
        var world = World();
        var anchor = 3000;

        var npc = NpcPopulation.RespawnNear(0, anchor, world, new StubRandomSource(0.1, 0.5, 0.9, 0.3));

        Assert.InRange(npc.CurrentYear, anchor - NpcPopulation.LocalSpawnSpreadYears, anchor + NpcPopulation.LocalSpawnSpreadYears);
        Assert.Equal(world.GetYear(npc.CurrentYear).Map.Start, npc.Position);
    }

    [Fact]
    public void RespawnNear_ClampsTheSpreadToTheTimelineBounds()
    {
        var world = World();

        var npcNearFloor = NpcPopulation.RespawnNear(0, TimeScale.MinYear, world, new StubRandomSource(0.0));
        var npcNearCeiling = NpcPopulation.RespawnNear(0, TimeScale.MaxYear, world, new StubRandomSource(0.99));

        Assert.InRange(npcNearFloor.CurrentYear, TimeScale.MinYear, TimeScale.MinYear + NpcPopulation.LocalSpawnSpreadYears);
        Assert.InRange(npcNearCeiling.CurrentYear, TimeScale.MaxYear - NpcPopulation.LocalSpawnSpreadYears, TimeScale.MaxYear);
    }

    [Fact]
    public void RespawnNear_KeepsTheSameNameForTheSamePopulationIndex()
    {
        var world = World();

        var first = NpcPopulation.RespawnNear(2, 2500, world, StubRandomSource.Fixed(0.5));
        var second = NpcPopulation.RespawnNear(2, 4000, world, StubRandomSource.Fixed(0.2));

        Assert.Equal(first.Name, second.Name);
    }

    [Fact]
    public void RespawnScattered_ProducesAYearAnywhereOnTheTimeline()
    {
        var world = World();

        var npc = NpcPopulation.RespawnScattered(0, world, new StubRandomSource(0.1, 0.5, 0.9));

        Assert.InRange(npc.CurrentYear, TimeScale.MinYear, TimeScale.MaxYear);
        Assert.Equal(world.GetYear(npc.CurrentYear).Map.Start, npc.Position);
    }

    [Fact]
    public void Spawn_WithNullClassWeights_MatchesOriginalUniformPickExactly()
    {
        // Regression guard: PickClass must still consume exactly one
        // NextDouble() call per NPC on the default (no classWeights) path,
        // so every pre-existing scripted-random test elsewhere in this
        // class keeps its exact assertions valid.
        var world = World();
        var sequence = new StubRandomSource(0.0, 0.25, 0.5, 0.75, 0.99, 0.1, 0.6);

        var withExplicitNull = NpcPopulation.Spawn(4, world, new StubRandomSource(0.0, 0.25, 0.5, 0.75, 0.99, 0.1, 0.6), classWeights: null);
        var withOmittedParam = NpcPopulation.Spawn(4, world, sequence);

        Assert.Equal(withOmittedParam.Select(n => n.CurrentYear), withExplicitNull.Select(n => n.CurrentYear));
        Assert.Equal(withOmittedParam.Select(n => n.Class), withExplicitNull.Select(n => n.Class));
        Assert.Equal(withOmittedParam.Select(n => n.Level), withExplicitNull.Select(n => n.Level));
    }

    [Fact]
    public void Spawn_WithASingleWeightedClass_AlwaysPicksThatClass()
    {
        var weights = new Dictionary<CharacterClass, double> { [CharacterClass.Doctor] = 3.0 };

        var npcs = NpcPopulation.Spawn(20, World(), new StubRandomSource(0.0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 0.99), weights);

        Assert.All(npcs, npc => Assert.Equal(CharacterClass.Doctor, npc.Class));
    }

    [Fact]
    public void Spawn_WithAClassMissingFromTheWeightTable_NeverPicksThatClass()
    {
        // Every class but Engineer gets a weight; Engineer should never appear.
        var weights = new Dictionary<CharacterClass, double>
        {
            [CharacterClass.Soldier] = 1,
            [CharacterClass.Spy] = 1,
            [CharacterClass.Doctor] = 1,
            [CharacterClass.Scientist] = 1,
        };

        var npcs = NpcPopulation.Spawn(40, World(), new StubRandomSource(0.02, 0.11, 0.23, 0.37, 0.49, 0.58, 0.66, 0.74, 0.81, 0.93), weights);

        Assert.DoesNotContain(npcs, npc => npc.Class == CharacterClass.Engineer);
    }

    [Fact]
    public void Spawn_WithAnEmptyWeightTable_FallsBackToUniformPick()
    {
        var uniform = NpcPopulation.Spawn(5, World(), new StubRandomSource(0.1, 0.5, 0.9, 0.2, 0.6));
        var withEmptyTable = NpcPopulation.Spawn(5, World(), new StubRandomSource(0.1, 0.5, 0.9, 0.2, 0.6), new Dictionary<CharacterClass, double>());

        Assert.Equal(uniform.Select(n => n.Class), withEmptyTable.Select(n => n.Class));
    }

    [Fact]
    public void Spawn_WithAnAllZeroWeightTable_FallsBackToUniformPick()
    {
        var allZero = new Dictionary<CharacterClass, double>
        {
            [CharacterClass.Soldier] = 0,
            [CharacterClass.Spy] = 0,
            [CharacterClass.Doctor] = 0,
            [CharacterClass.Scientist] = 0,
            [CharacterClass.Engineer] = 0,
        };

        var uniform = NpcPopulation.Spawn(5, World(), new StubRandomSource(0.1, 0.5, 0.9, 0.2, 0.6));
        var withAllZero = NpcPopulation.Spawn(5, World(), new StubRandomSource(0.1, 0.5, 0.9, 0.2, 0.6), allZero);

        Assert.Equal(uniform.Select(n => n.Class), withAllZero.Select(n => n.Class));
    }

    [Fact]
    public void RespawnNear_ThreadsClassWeightsThrough()
    {
        var weights = new Dictionary<CharacterClass, double> { [CharacterClass.Scientist] = 1 };

        var npc = NpcPopulation.RespawnNear(0, 3000, World(), new StubRandomSource(0.2, 0.4, 0.6), weights);

        Assert.Equal(CharacterClass.Scientist, npc.Class);
    }

    [Fact]
    public void RespawnScattered_ThreadsClassWeightsThrough()
    {
        var weights = new Dictionary<CharacterClass, double> { [CharacterClass.Spy] = 1 };

        var npc = NpcPopulation.RespawnScattered(0, World(), new StubRandomSource(0.3, 0.5, 0.7), weights);

        Assert.Equal(CharacterClass.Spy, npc.Class);
    }

    // --- CreatureTraitKind spawn roll ---------------------------------------

    [Fact]
    public void Spawn_TraitRollPasses_AssignsATraitFromTheNpcPool()
    {
        // First NextDouble() -> class pick; second (and repeated, since the
        // stub replays its last value) -> the 40% trait gate, well under it.
        var npcs = NpcPopulation.Spawn(10, World(), new StubRandomSource(0.5, 0.1));

        Assert.All(npcs, npc => Assert.Contains(npc.Trait, CreatureTraits.NpcPool));
    }

    [Fact]
    public void Spawn_TraitRollFails_LeavesTraitNone()
    {
        var npcs = NpcPopulation.Spawn(10, World(), new StubRandomSource(0.5, 0.9)); // 0.9 misses the 40% gate

        Assert.All(npcs, npc => Assert.Equal(CreatureTraitKind.None, npc.Trait));
    }

    [Fact]
    public void RespawnNear_AlsoRollsATrait()
    {
        var npc = NpcPopulation.RespawnNear(0, 3000, World(), new StubRandomSource(0.5, 0.1));

        Assert.Contains(npc.Trait, CreatureTraits.NpcPool);
    }
}
