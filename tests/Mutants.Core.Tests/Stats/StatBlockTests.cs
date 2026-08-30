using Mutants.Core.Classes;
using Mutants.Core.Stats;

namespace Mutants.Core.Tests.Stats;

public class StatBlockTests
{
    [Fact]
    public void Get_ReturnsCorrectStatPerEnum()
    {
        var stats = new StatBlock(Strength: 1, Agility: 2, Faith: 3, Intellect: 4);

        Assert.Equal(1, stats.Get(PrimaryStat.Strength));
        Assert.Equal(2, stats.Get(PrimaryStat.Agility));
        Assert.Equal(3, stats.Get(PrimaryStat.Faith));
        Assert.Equal(4, stats.Get(PrimaryStat.Intellect));
    }

    [Fact]
    public void Increase_OnlyChangesTargetedStat()
    {
        var stats = new StatBlock(Strength: 10, Agility: 10, Faith: 10, Intellect: 10);
        var increased = stats.Increase(PrimaryStat.Faith, 3);

        Assert.Equal(10, increased.Strength);
        Assert.Equal(10, increased.Agility);
        Assert.Equal(13, increased.Faith);
        Assert.Equal(10, increased.Intellect);
    }

    [Fact]
    public void Increase_IsImmutable()
    {
        var original = new StatBlock(Strength: 10, Agility: 10, Faith: 10, Intellect: 10);
        _ = original.Increase(PrimaryStat.Strength, 5);

        Assert.Equal(10, original.Strength);
    }
}
