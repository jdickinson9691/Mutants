using ChronoTravelers.Core.Stats;

namespace ChronoTravelers.Core.Tests.Stats;

public class LevelingTests
{
    [Fact]
    public void CumulativeXpForLevel_LevelOneIsFree()
    {
        Assert.Equal(0, Leveling.CumulativeXpForLevel(1));
    }

    [Fact]
    public void CumulativeXpForLevel_IsStrictlyIncreasing()
    {
        var previous = Leveling.CumulativeXpForLevel(1);
        for (var level = 2; level <= Leveling.MaxCharacterLevel; level++)
        {
            var current = Leveling.CumulativeXpForLevel(level);
            Assert.True(current > previous, $"XP for level {level} ({current}) should exceed level {level - 1} ({previous}).");
            previous = current;
        }
    }

    [Fact]
    public void CumulativeXpForLevel_RejectsLevelBelowOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Leveling.CumulativeXpForLevel(0));
    }

    [Fact]
    public void CumulativeXpForLevel_QuadraticThroughKnee()
    {
        // Each step below the knee costs 100 more than the one before it.
        for (var level = 3; level <= Leveling.XpCurveKneeLevel; level++)
        {
            var stepHere = Leveling.CumulativeXpForLevel(level) - Leveling.CumulativeXpForLevel(level - 1);
            var stepBefore = Leveling.CumulativeXpForLevel(level - 1) - Leveling.CumulativeXpForLevel(level - 2);
            Assert.Equal(100, stepHere - stepBefore);
        }
    }

    [Fact]
    public void CumulativeXpForLevel_FlatPerLevelPastKnee()
    {
        // Past the knee the per-level cost holds constant, so the deep game
        // is a linear grind rather than a quadratic wall.
        var kneeStep = 100 * Leveling.XpCurveKneeLevel;
        for (var level = Leveling.XpCurveKneeLevel + 1; level <= Leveling.MaxCharacterLevel; level++)
        {
            var step = Leveling.CumulativeXpForLevel(level) - Leveling.CumulativeXpForLevel(level - 1);
            Assert.Equal(kneeStep, step);
        }
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 30)]
    public void SoftLevelCap_IsTenTimesUnlockedTimeLevel(int unlockedTimeLevel, int expectedCap)
    {
        Assert.Equal(expectedCap, Leveling.SoftLevelCap(unlockedTimeLevel));
    }

    [Fact]
    public void SoftLevelCap_NeverExceedsMaxCharacterLevel()
    {
        Assert.Equal(Leveling.MaxCharacterLevel, Leveling.SoftLevelCap(unlockedTimeLevel: 10));
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(10, true)]
    [InlineData(30, true)]
    [InlineData(4, false)]
    [InlineData(6, false)]
    [InlineData(31, false)]
    // Levels past the 6th ability tier are valid (cap is 60) but unlock no
    // new abilities — stats/HP only — so a fifth-multiple past 30 is false.
    [InlineData(35, false)]
    [InlineData(60, false)]
    public void UnlocksAbilityTier_OnlyOnMultiplesOfFiveUpToTopAbilityLevel(int level, bool expected)
    {
        Assert.Equal(expected, Leveling.UnlocksAbilityTier(level));
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(10, 2)]
    [InlineData(30, 6)]
    public void AbilityTierUnlockedAt_MapsLevelToTierIndex(int level, int expectedTier)
    {
        Assert.Equal(expectedTier, Leveling.AbilityTierUnlockedAt(level));
    }

    [Fact]
    public void AbilityTierUnlockedAt_NullOnNonGatingLevel()
    {
        Assert.Null(Leveling.AbilityTierUnlockedAt(7));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(4, 0)]
    [InlineData(5, 1)]
    [InlineData(24, 4)]
    [InlineData(30, 6)]
    [InlineData(60, 6)]
    public void UnlockedAbilityTierCount_TracksFloorDivisionByFive(int level, int expectedCount)
    {
        Assert.Equal(expectedCount, Leveling.UnlockedAbilityTierCount(level));
    }
}
