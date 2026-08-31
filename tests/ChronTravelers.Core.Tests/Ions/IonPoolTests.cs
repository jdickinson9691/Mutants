using ChronTravelers.Core.Ions;

namespace ChronTravelers.Core.Tests.Ions;

public class IonPoolTests
{
    [Fact]
    public void NewPool_DefaultsCurrentToMax()
    {
        var pool = new IonPool(max: 50);
        Assert.Equal(50, pool.Current);
        Assert.Equal(50, pool.Max);
    }

    [Fact]
    public void Add_ClampsAtMax()
    {
        var pool = new IonPool(max: 20, current: 15);
        var added = pool.Add(10);

        Assert.Equal(20, pool.Current);
        Assert.Equal(5, added);
    }

    [Fact]
    public void CanAfford_ReflectsCurrentBalance()
    {
        var pool = new IonPool(max: 20, current: 10);

        Assert.True(pool.CanAfford(10));
        Assert.False(pool.CanAfford(11));
    }

    [Fact]
    public void Spend_ReducesCurrent()
    {
        var pool = new IonPool(max: 20, current: 10);
        pool.Spend(7);
        Assert.Equal(3, pool.Current);
    }

    [Fact]
    public void Spend_ThrowsWhenUnaffordable()
    {
        var pool = new IonPool(max: 20, current: 5);
        Assert.Throws<InvalidOperationException>(() => pool.Spend(6));
    }

    [Fact]
    public void SetMax_LoweringClampsCurrentDown()
    {
        var pool = new IonPool(max: 20, current: 20);
        pool.SetMax(10);
        Assert.Equal(10, pool.Max);
        Assert.Equal(10, pool.Current);
    }

    [Fact]
    public void SetMax_RaisingDoesNotChangeCurrent()
    {
        var pool = new IonPool(max: 20, current: 5);
        pool.SetMax(30);
        Assert.Equal(30, pool.Max);
        Assert.Equal(5, pool.Current);
    }

    // --- uncapped (player) pools ------------------------------------------

    [Fact]
    public void Uncapped_AddDoesNotClamp_AndCurrentMayExceedMax()
    {
        var pool = new IonPool(max: 20, current: 18, uncapped: true);

        Assert.Equal(50, pool.Add(50));
        Assert.Equal(68, pool.Current);
        Assert.Equal(20, pool.Max); // nominal, unchanged
    }

    [Fact]
    public void Uncapped_RespectSoftCap_StillClampsAtMax()
    {
        var pool = new IonPool(max: 20, current: 19, uncapped: true);

        Assert.Equal(1, pool.Add(10, respectSoftCap: true)); // passive regen
        Assert.Equal(20, pool.Current);
        Assert.Equal(0, pool.Add(10, respectSoftCap: true)); // already at the soft cap
    }

    [Fact]
    public void Uncapped_ConstructorKeepsACurrentAboveMax()
    {
        var pool = new IonPool(max: 20, current: 95, uncapped: true);
        Assert.Equal(95, pool.Current);
    }

    [Fact]
    public void Uncapped_SetMaxNeverDragsCurrentDown()
    {
        var pool = new IonPool(max: 20, current: 80, uncapped: true);
        pool.SetMax(10);
        Assert.Equal(10, pool.Max);
        Assert.Equal(80, pool.Current);
    }
}
