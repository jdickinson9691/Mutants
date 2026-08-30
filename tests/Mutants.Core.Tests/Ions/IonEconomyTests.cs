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
    [InlineData(1, 25)]
    [InlineData(2, 50)]
    [InlineData(5, 125)]
    [InlineData(10, 250)]
    public void TimeTravelCost_IsTwentyFiveTimesTargetLevel(int targetLevel, int expectedCost)
    {
        Assert.Equal(expectedCost, IonEconomy.TimeTravelCost(targetLevel));
    }

    [Fact]
    public void TimeTravelCost_RejectsLevelBelowOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IonEconomy.TimeTravelCost(0));
    }

    [Fact]
    public void TicksPerIonDrain_TightensAtDeeperTimeLevels()
    {
        var shallow = IonEconomy.TicksPerIonDrain(timeLevel: 1, classDrainMultiplier: 1.0);
        var deep = IonEconomy.TicksPerIonDrain(timeLevel: 5, classDrainMultiplier: 1.0);

        Assert.True(deep <= shallow, "Deeper time levels should drain Ions at least as fast as shallow ones.");
    }

    [Fact]
    public void TicksPerIonDrain_HigherClassMultiplierDrainsFaster()
    {
        var slowClass = IonEconomy.TicksPerIonDrain(timeLevel: 1, classDrainMultiplier: 0.8); // e.g. Warrior
        var fastClass = IonEconomy.TicksPerIonDrain(timeLevel: 1, classDrainMultiplier: 1.3); // e.g. Mage

        Assert.True(fastClass <= slowClass, "A higher drain multiplier should mean fewer ticks per Ion (faster drain).");
    }

    [Fact]
    public void TicksPerIonDrain_NeverGoesBelowOne()
    {
        var result = IonEconomy.TicksPerIonDrain(timeLevel: 50, classDrainMultiplier: 5.0);
        Assert.True(result >= 1);
    }
}
