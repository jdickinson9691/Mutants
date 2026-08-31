using Mutants.Core.Ions;

namespace Mutants.Core.Tests.Ions;

public class IonEconomyTests
{
    [Theory]
    [InlineData(0, 1)]   // minimum of 1 even for a worthless item
    [InlineData(1, 1)]
    [InlineData(2, 1)]   // floor(2 * 0.4) = 0 -> clamped to minimum 1
    [InlineData(10, 4)]  // floor(10 * 0.4) = 4
    [InlineData(25, 10)] // floor(25 * 0.4) = 10
    [InlineData(100, 40)]
    public void ConvertValue_IsFortyPercentOfBaseValueFlooredWithMinimumOne(int baseValue, int expected)
    {
        Assert.Equal(expected, IonEconomy.ConvertValue(baseValue));
    }

    [Fact]
    public void ConvertValue_RejectsNegativeValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IonEconomy.ConvertValue(-1));
    }

    [Theory]
    [InlineData(2000, 2000, 0)]     // staying put is free
    [InlineData(2000, 2100, 20)]    // ceil(0.2 * 100)
    [InlineData(2000, 2500, 100)]
    [InlineData(2500, 2000, 100)]   // symmetric — retreating costs the same
    [InlineData(2000, 2003, 1)]     // ceil(0.2 * 3) = 1
    [InlineData(2000, 5000, 600)]
    public void TimeTravelCost_IsCeilOfPointTwoTimesTheYearDistance(int fromYear, int toYear, int expectedCost)
    {
        Assert.Equal(expectedCost, IonEconomy.TimeTravelCost(fromYear, toYear));
    }

    [Fact]
    public void TicksPerIonDrain_TightensAtHigherScalingTiers()
    {
        var low = IonEconomy.TicksPerIonDrain(scalingTier: 1, classDrainMultiplier: 1.0);
        var high = IonEconomy.TicksPerIonDrain(scalingTier: 5, classDrainMultiplier: 1.0);

        Assert.True(high <= low, "Higher scaling tiers should drain Ions at least as fast as lower ones.");
    }

    [Fact]
    public void TicksPerIonDrain_HigherClassMultiplierDrainsFaster()
    {
        var slowClass = IonEconomy.TicksPerIonDrain(scalingTier: 1, classDrainMultiplier: 0.8); // e.g. Warrior
        var fastClass = IonEconomy.TicksPerIonDrain(scalingTier: 1, classDrainMultiplier: 1.3); // e.g. Mage

        Assert.True(fastClass <= slowClass, "A higher drain multiplier should mean fewer ticks per Ion (faster drain).");
    }

    [Fact]
    public void TicksPerIonDrain_NeverGoesBelowOne()
    {
        var result = IonEconomy.TicksPerIonDrain(scalingTier: 50, classDrainMultiplier: 5.0);
        Assert.True(result >= 1);
    }
}
