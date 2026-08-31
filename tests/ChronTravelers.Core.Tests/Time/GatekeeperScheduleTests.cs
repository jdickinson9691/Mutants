using ChronTravelers.Core.Time;

namespace ChronTravelers.Core.Tests.Time;

public class GatekeeperScheduleTests
{
    [Fact]
    public void EveryGapBetweenGatekeeperYearsIsBetween50And100()
    {
        var schedule = new GatekeeperSchedule(worldSeed: 12345);
        var years = schedule.Years.ToList();

        Assert.NotEmpty(years);

        var previous = TimeScale.MinYear;
        foreach (var year in years)
        {
            var gap = year - previous;
            Assert.InRange(gap, GatekeeperSchedule.MinGap, GatekeeperSchedule.MaxGap);
            previous = year;
        }
    }

    [Fact]
    public void AllGatekeeperYearsFallInsideTheTimelineAndExcludeYear2000()
    {
        var schedule = new GatekeeperSchedule(worldSeed: 777);

        Assert.All(schedule.Years, y => Assert.InRange(y, TimeScale.MinYear + 1, TimeScale.MaxYear));
    }

    [Fact]
    public void SameSeedProducesTheSameSchedule()
    {
        var a = new GatekeeperSchedule(worldSeed: 42).Years.ToArray();
        var b = new GatekeeperSchedule(worldSeed: 42).Years.ToArray();

        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentSeedsProduceDifferentSchedules()
    {
        var a = new GatekeeperSchedule(worldSeed: 1).Years.ToArray();
        var b = new GatekeeperSchedule(worldSeed: 2).Years.ToArray();

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void NextAfterAndPreviousBefore_WalkTheSchedule()
    {
        var schedule = new GatekeeperSchedule(worldSeed: 99);
        var years = schedule.Years.ToList();

        var first = years[0];
        Assert.Equal(first, schedule.NextAfter(TimeScale.MinYear));
        Assert.Null(schedule.PreviousBefore(first));
        Assert.Equal(first, schedule.PreviousBefore(years[1]));
        Assert.Null(schedule.NextAfter(years[^1]));
    }

    [Fact]
    public void Between_ReturnsOnlyGatekeeperYearsInRange()
    {
        var schedule = new GatekeeperSchedule(worldSeed: 5);
        var years = schedule.Years.ToList();
        var lo = years[1];
        var hi = years[^2];

        var slice = schedule.Between(hi, lo).ToList(); // order of args shouldn't matter

        Assert.All(slice, y => Assert.InRange(y, lo, hi));
        Assert.Equal(slice.OrderBy(y => y).ToArray(), slice.ToArray());
    }
}
