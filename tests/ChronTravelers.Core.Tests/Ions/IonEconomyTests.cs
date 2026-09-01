using ChronTravelers.Core.Ions;

namespace ChronTravelers.Core.Tests.Ions;

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
    [InlineData(2000, 2005, 8)]     // ceil(0.04 * 5) = 1, floored to MinTravelCost
    [InlineData(2000, 2100, 8)]     // ceil(0.04 * 100) = 4, floored to MinTravelCost
    [InlineData(2000, 2200, 8)]     // ceil(0.04 * 200) = 8, exactly the floor
    [InlineData(2000, 2500, 20)]    // above the floor — linear rate applies
    [InlineData(2500, 2000, 20)]    // symmetric — retreating costs the same
    [InlineData(2000, 5000, 120)]
    public void TimeTravelCost_IsCeilOfTheCoefficientTimesDistance_FlooredAtMinTravelCost(int fromYear, int toYear, int expectedCost)
    {
        Assert.Equal(expectedCost, IonEconomy.TimeTravelCost(fromYear, toYear));
    }

    [Fact]
    public void TicksPerIonRegen_IsFasterInThePresentThanInTheFarFuture()
    {
        var early = IonEconomy.TicksPerIonRegen(scalingTier: 1, classDrainMultiplier: 1.0);
        var late = IonEconomy.TicksPerIonRegen(scalingTier: 9, classDrainMultiplier: 1.0);

        Assert.True(early < late, "Ions should regen faster in early years.");
    }

    [Fact]
    public void TicksPerIonRegen_OutpacesTheDrainInEarlyYearsAndFallsBehindInLateYears()
    {
        // Early: regen cadence shorter than drain cadence -> net Ion gain.
        Assert.True(IonEconomy.TicksPerIonRegen(1, 1.0) < IonEconomy.TicksPerIonDrain(1, 1.0));
        // Late: regen cadence longer than drain cadence -> net Ion loss.
        Assert.True(IonEconomy.TicksPerIonRegen(9, 1.0) > IonEconomy.TicksPerIonDrain(9, 1.0));
    }

    [Fact]
    public void TicksPerIonRegen_HigherClassMultiplierRegensSlower()
    {
        var slowDrainClass = IonEconomy.TicksPerIonRegen(scalingTier: 1, classDrainMultiplier: 0.8);
        var fastDrainClass = IonEconomy.TicksPerIonRegen(scalingTier: 1, classDrainMultiplier: 1.3);

        Assert.True(fastDrainClass >= slowDrainClass);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TicksPerIonRegen_RejectsScalingTierBelowOne(int tier)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IonEconomy.TicksPerIonRegen(tier, 1.0));
    }

    [Fact]
    public void HpPerIonHealed_IsThreeToOne()
    {
        Assert.Equal(3, IonEconomy.HpPerIonHealed);
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
        var slowClass = IonEconomy.TicksPerIonDrain(scalingTier: 1, classDrainMultiplier: 0.8); // e.g. Soldier
        var fastClass = IonEconomy.TicksPerIonDrain(scalingTier: 1, classDrainMultiplier: 1.3); // e.g. Scientist

        Assert.True(fastClass <= slowClass, "A higher drain multiplier should mean fewer ticks per Ion (faster drain).");
    }

    [Fact]
    public void TicksPerIonDrain_NeverGoesBelowOne()
    {
        var result = IonEconomy.TicksPerIonDrain(scalingTier: 50, classDrainMultiplier: 5.0);
        Assert.True(result >= 1);
    }
}
