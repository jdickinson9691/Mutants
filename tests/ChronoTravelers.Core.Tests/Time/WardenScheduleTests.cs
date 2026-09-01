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
            var gap = year - previous;
            Assert.InRange(gap, WardenSchedule.MinGap, WardenSchedule.MaxGap);
            previous = year;
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
