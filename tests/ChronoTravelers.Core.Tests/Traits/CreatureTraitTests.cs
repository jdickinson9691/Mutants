using ChronoTravelers.Core.Traits;

namespace ChronoTravelers.Core.Tests.Traits;

public class CreatureTraitTests
{
    [Fact]
    public void MonsterPool_HasAllEightRealTraits_AndNeverNone()
    {
        var realTraits = Enum.GetValues<CreatureTraitKind>().Where(k => k != CreatureTraitKind.None);

        Assert.Equal(8, CreatureTraits.MonsterPool.Count);
        Assert.Equal(realTraits.OrderBy(k => k), CreatureTraits.MonsterPool.Distinct().OrderBy(k => k));
        Assert.DoesNotContain(CreatureTraitKind.None, CreatureTraits.MonsterPool);
    }

    [Fact]
    public void NpcPool_HasAllEightRealTraits_AndNeverNone()
    {
        var realTraits = Enum.GetValues<CreatureTraitKind>().Where(k => k != CreatureTraitKind.None);

        Assert.Equal(8, CreatureTraits.NpcPool.Count);
        Assert.Equal(realTraits.OrderBy(k => k), CreatureTraits.NpcPool.Distinct().OrderBy(k => k));
        Assert.DoesNotContain(CreatureTraitKind.None, CreatureTraits.NpcPool);
    }

    [Fact]
    public void Name_and_Description_AreNonEmptyForEveryRealTrait()
    {
        foreach (var kind in Enum.GetValues<CreatureTraitKind>())
        {
            if (kind == CreatureTraitKind.None)
            {
                continue;
            }

            Assert.False(string.IsNullOrWhiteSpace(kind.Name()));
            Assert.False(string.IsNullOrWhiteSpace(kind.Description()));
        }
    }

    [Fact]
    public void RollForSpawn_BelowSpawnChance_PicksFromThePool()
    {
        // First call (0.1) clears the 40% gate; second call (0.1) picks index 0.
        var result = CreatureTraits.RollForSpawn(CreatureTraits.MonsterPool, Sequence(0.1, 0.1));

        Assert.Equal(CreatureTraits.MonsterPool[0], result);
    }

    [Fact]
    public void RollForSpawn_AtOrAboveSpawnChance_ReturnsNone()
    {
        var result = CreatureTraits.RollForSpawn(CreatureTraits.MonsterPool, Sequence(CreatureTraits.SpawnChance));

        Assert.Equal(CreatureTraitKind.None, result);
    }

    [Fact]
    public void RollForSpawn_JustBelowSpawnChance_StillPicksFromThePool()
    {
        var result = CreatureTraits.RollForSpawn(CreatureTraits.MonsterPool, Sequence(CreatureTraits.SpawnChance - 0.0001, 0.0));

        Assert.NotEqual(CreatureTraitKind.None, result);
    }

    [Fact]
    public void RollForSpawn_EmptyPool_AlwaysReturnsNoneWithoutConsumingASecondRoll()
    {
        var calls = 0;
        double NextDouble()
        {
            calls++;
            return 0.0; // would pass the gate if the pool weren't empty
        }

        var result = CreatureTraits.RollForSpawn([], NextDouble);

        Assert.Equal(CreatureTraitKind.None, result);
        Assert.Equal(1, calls); // only the gate check - never tries to index an empty pool
    }

    [Fact]
    public void RollForSpawn_OverManyRolls_LandsRoughlyOnTheTunedSpawnChance()
    {
        var random = new Random(12345);
        var hits = 0;
        const int trials = 20000;

        for (var i = 0; i < trials; i++)
        {
            if (CreatureTraits.RollForSpawn(CreatureTraits.NpcPool, random.NextDouble) != CreatureTraitKind.None)
            {
                hits++;
            }
        }

        var rate = hits / (double)trials;
        Assert.InRange(rate, CreatureTraits.SpawnChance - 0.03, CreatureTraits.SpawnChance + 0.03);
    }

    /// <summary>A scripted <c>Func&lt;double&gt;</c> — repeats its last value once exhausted, matching <c>StubRandomSource</c>'s own convention, so tests don't need to over-specify length.</summary>
    private static Func<double> Sequence(params double[] values)
    {
        var index = 0;
        return () =>
        {
            var value = values[Math.Min(index, values.Length - 1)];
            index++;
            return value;
        };
    }
}
