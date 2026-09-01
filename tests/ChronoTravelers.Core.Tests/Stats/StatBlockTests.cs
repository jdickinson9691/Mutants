using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Stats;

namespace ChronoTravelers.Core.Tests.Stats;

public class StatBlockTests
{
    [Fact]
    public void Get_ReturnsCorrectStatPerEnum()
    {
        var stats = new StatBlock(Strength: 1, Agility: 2, Resolve: 3, Intellect: 4);

        Assert.Equal(1, stats.Get(PrimaryStat.Strength));
        Assert.Equal(2, stats.Get(PrimaryStat.Agility));
        Assert.Equal(3, stats.Get(PrimaryStat.Resolve));
        Assert.Equal(4, stats.Get(PrimaryStat.Intellect));
    }

    [Fact]
    public void Increase_OnlyChangesTargetedStat()
    {
        var stats = new StatBlock(Strength: 10, Agility: 10, Resolve: 10, Intellect: 10);
        var increased = stats.Increase(PrimaryStat.Resolve, 3);

        Assert.Equal(10, increased.Strength);
        Assert.Equal(10, increased.Agility);
        Assert.Equal(13, increased.Resolve);
        Assert.Equal(10, increased.Intellect);
    }

    [Fact]
    public void Increase_IsImmutable()
    {
        var original = new StatBlock(Strength: 10, Agility: 10, Resolve: 10, Intellect: 10);
        _ = original.Increase(PrimaryStat.Strength, 5);

        Assert.Equal(10, original.Strength);
    }

    [Fact]
    public void LevelUp_BumpsThePrimaryFaster_AndEveryOtherStatToo()
    {
        var stats = new StatBlock(Strength: 10, Agility: 10, Resolve: 10, Intellect: 10);

        var next = stats.LevelUp(PrimaryStat.Agility, primaryGain: 5, otherGain: 2);

        Assert.Equal(15, next.Agility);      // primary
        Assert.Equal(12, next.Strength);     // secondary
        Assert.Equal(12, next.Resolve);
        Assert.Equal(12, next.Intellect);
        Assert.Equal(10, stats.Agility);     // original untouched
    }
}
