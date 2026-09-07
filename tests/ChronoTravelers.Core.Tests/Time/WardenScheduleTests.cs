using ChronoTravelers.Core.Time;

namespace ChronoTravelers.Core.Tests.Time;

public class WardenScheduleTests
{
    [Fact]
    public void EveryGapBetweenWardenYearsIsBetween50And100()
    {
        var schedule = new WardenSchedule(worldSeed: 12345);
        var years = schedule.Years.ToList();

        Assert.NotEmpty(years);

        var previous = TimeScale.MinYear;
        foreach (var year in years)
        {
            // Year 5000 is a guaranteed capstone (docs/ENDGAME_STRATEGY.md
            // recommendation 4) forced in regardless of the random walk's
            // gap, so it's deliberately exempt from the 50-100 gap rule —
            // see Year5000IsAlwaysAWardenYearRegardlessOfSeed below.
            if (year == TimeScale.MaxYear)
            {
                continue;
            }

            var gap = year - previous;
            Assert.InRange(gap, WardenSchedule.MinGap, WardenSchedule.MaxGap);
            previous = year;
        }
    }

    [Fact]
    public void Year5000IsAlwaysAWardenYearRegardlessOfSeed()
    {
        foreach (var seed in new long[] { 1, 2, 42, 12345, 777, 999999 })
        {
            var schedule = new WardenSchedule(seed);
            Assert.True(schedule.IsWardenYear(TimeScale.MaxYear), $"seed {seed} should always include the year-5000 capstone");
        }
    }

    [Fact]
    public void AllWardenYearsFallInsideTheTimelineAndExcludeYear2000()
    {
        var schedule = new WardenSchedule(worldSeed: 777);

        Assert.All(schedule.Years, y => Assert.InRange(y, TimeScale.MinYear + 1, TimeScale.MaxYear));
    }

    [Fact]
    public void SameSeedProducesTheSameSchedule()
    {
        var a = new WardenSchedule(worldSeed: 42).Years.ToArray();
        var b = new WardenSchedule(worldSeed: 42).Years.ToArray();

        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentSeedsProduceDifferentSchedules()
    {
        var a = new WardenSchedule(worldSeed: 1).Years.ToArray();
        var b = new WardenSchedule(worldSeed: 2).Years.ToArray();

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void NextAfterAndPreviousBefore_WalkTheSchedule()
    {
        var schedule = new WardenSchedule(worldSeed: 99);
        var years = schedule.Years.ToList();

        var first = years[0];
        Assert.Equal(first, schedule.NextAfter(TimeScale.MinYear));
        Assert.Null(schedule.PreviousBefore(first));
        Assert.Equal(first, schedule.PreviousBefore(years[1]));
        Assert.Null(schedule.NextAfter(years[^1]));
    }

    [Fact]
    public void Between_ReturnsOnlyWardenYearsInRange()
    {
        var schedule = new WardenSchedule(worldSeed: 5);
        var years = schedule.Years.ToList();
        var lo = years[1];
        var hi = years[^2];

        var slice = schedule.Between(hi, lo).ToList(); // order of args shouldn't matter

        Assert.All(slice, y => Assert.InRange(y, lo, hi));
        Assert.Equal(slice.OrderBy(y => y).ToArray(), slice.ToArray());
    }
}
