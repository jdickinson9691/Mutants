using Mutants.Core.Stats;
using Mutants.Core.Time;

namespace Mutants.Core.Tests.Time;

public class TimeScaleTests
{
    [Theory]
    [InlineData(2000, 1.0)]
    [InlineData(2375, 2.0)]
    [InlineData(2750, 3.0)]
    [InlineData(5000, 9.0)]
    public void TierForYear_MapsTheTimelineOntoTiersOneThroughNine(int year, double expectedTier)
    {
        Assert.Equal(expectedTier, TimeScale.TierForYear(year), precision: 6);
    }

    [Fact]
    public void TierForYear_IsStrictlyIncreasingAcrossTheTimeline()
    {
        var previous = double.MinValue;
        for (var year = TimeScale.MinYear; year <= TimeScale.MaxYear; year += 25)
        {
            var tier = TimeScale.TierForYear(year);
            Assert.True(tier > previous, $"tier at {year} ({tier}) was not greater than the previous ({previous}).");
            previous = tier;
        }
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(5001)]
    [InlineData(0)]
    public void TierForYear_RejectsYearsOutsideTheTimeline(int year)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TimeScale.TierForYear(year));
    }

    [Theory]
    [InlineData(2000, 10)]
    [InlineData(2375, 20)]
    [InlineData(2750, 30)]
    [InlineData(5000, 30)]
    public void SoftLevelCapForYear_TracksTheYearButNeverExceedsTheHardCap(int year, int expectedCap)
    {
        Assert.Equal(expectedCap, TimeScale.SoftLevelCapForYear(year));
        Assert.True(TimeScale.SoftLevelCapForYear(year) <= Leveling.MaxCharacterLevel);
        Assert.True(TimeScale.SoftLevelCapForYear(year) >= 10);
    }

    [Theory]
    [InlineData(1999, false)]
    [InlineData(2000, true)]
    [InlineData(3500, true)]
    [InlineData(5000, true)]
    [InlineData(5001, false)]
    public void IsValidYear_BoundsTheTimelineInclusive(int year, bool expected)
    {
        Assert.Equal(expected, TimeScale.IsValidYear(year));
    }
}
