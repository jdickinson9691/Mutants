using ChronTravelers.Core.Stats;

namespace ChronTravelers.Core.Tests.Stats;

public class HealthPoolTests
{
    [Fact]
    public void Damage_ClampsAtZero_AndReportsActualDamageTaken()
    {
        var hp = new HealthPool(max: 30, current: 5);
        var taken = hp.Damage(20);

        Assert.Equal(0, hp.Current);
        Assert.Equal(5, taken);
        Assert.True(hp.IsDead);
    }

    [Fact]
    public void Heal_ClampsAtMax_AndReportsActualHealAmount()
    {
        var hp = new HealthPool(max: 30, current: 25);
        var healed = hp.Heal(20);

        Assert.Equal(30, hp.Current);
        Assert.Equal(5, healed);
        Assert.False(hp.IsDead);
    }

    [Fact]
    public void IsDead_TrueOnlyAtZero()
    {
        var hp = new HealthPool(max: 10, current: 1);
        Assert.False(hp.IsDead);

        hp.Damage(1);
        Assert.True(hp.IsDead);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveMax()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HealthPool(max: 0));
    }
}
