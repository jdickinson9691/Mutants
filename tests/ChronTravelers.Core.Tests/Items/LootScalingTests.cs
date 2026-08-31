using ChronTravelers.Core.Items;

namespace ChronTravelers.Core.Tests.Items;

public class LootScalingTests
{
    [Theory]
    [InlineData(1, 22)]   // 12*1 + 10 — front-loaded early bump
    [InlineData(2, 34)]
    [InlineData(10, 130)]
    public void TierBaseValue_IsTwelveTimesTierPlusTen(int tier, int expected)
    {
        Assert.Equal(expected, LootScaling.TierBaseValue(tier));
    }

    [Fact]
    public void TierBaseValue_RejectsTierBelowOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LootScaling.TierBaseValue(0));
    }

    [Fact]
    public void ValueFor_AppliesRarityMultiplierToTierBaseline()
    {
        Assert.Equal(22, LootScaling.ValueFor(tier: 1, Rarity.Common));
        Assert.Equal(44, LootScaling.ValueFor(tier: 1, Rarity.Legendary));
    }

    [Fact]
    public void CombatBonusFor_ScalesWithTierAndRarity()
    {
        var lowTierCommon = LootScaling.CombatBonusFor(tier: 1, Rarity.Common);
        var highTierCommon = LootScaling.CombatBonusFor(tier: 5, Rarity.Common);
        var lowTierLegendary = LootScaling.CombatBonusFor(tier: 1, Rarity.Legendary);

        Assert.True(highTierCommon > lowTierCommon);
        Assert.True(lowTierLegendary > lowTierCommon);
    }

    [Fact]
    public void DoubleOverloads_AreContinuousBetweenWholeTiers()
    {
        Assert.Equal(40.0, LootScaling.TierBaseValue(2.5), precision: 6); // 12*2.5 + 10

        var atTwo = LootScaling.ValueFor(2.0, Rarity.Common);
        var atHalf = LootScaling.ValueFor(2.5, Rarity.Common);
        var atThree = LootScaling.ValueFor(3.0, Rarity.Common);
        Assert.True(atHalf > atTwo && atHalf < atThree);
    }

    [Fact]
    public void IntOverloads_MatchDoubleOverloadsRoundedAtWholeTiers()
    {
        foreach (var rarity in System.Enum.GetValues<Rarity>())
        {
            for (var tier = 1; tier <= 9; tier++)
            {
                Assert.Equal(LootScaling.ValueFor(tier, rarity), (int)System.Math.Round(LootScaling.ValueFor((double)tier, rarity)));
                Assert.Equal(LootScaling.CombatBonusFor(tier, rarity), (int)System.Math.Round(LootScaling.CombatBonusFor((double)tier, rarity)));
            }
        }
    }

    [Fact]
    public void DoubleTierBaseValue_RejectsTierBelowOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LootScaling.TierBaseValue(0.5));
    }

    // --- power-band equip scaling (weapons / armour / ranged) ----------------

    [Fact]
    public void EquipBonusFor_ScalesWithBothTierAndPowerMultiplier()
    {
        var crudeEarly = LootScaling.EquipBonusFor(tier: 1, powerMultiplier: 0.5);
        var crudeLate = LootScaling.EquipBonusFor(tier: 9, powerMultiplier: 0.5);
        var relicEarly = LootScaling.EquipBonusFor(tier: 1, powerMultiplier: 2.9);

        Assert.True(crudeLate > crudeEarly, "same weapon class, later year -> bigger bonus");
        Assert.True(relicEarly > crudeEarly * 3, "a relic at least triples a crude weapon of the same tier");
    }

    [Fact]
    public void EquipBonusFor_ClampsThePowerMultiplierToItsBand()
    {
        Assert.Equal(
            LootScaling.EquipBonusFor(3.0, LootScaling.MaxPowerMultiplier),
            LootScaling.EquipBonusFor(3.0, 99.0), precision: 6);
        Assert.Equal(
            LootScaling.EquipBonusFor(3.0, LootScaling.MinPowerMultiplier),
            LootScaling.EquipBonusFor(3.0, 0.01), precision: 6);
    }

    [Theory]
    [InlineData(0.5, Rarity.Common)]
    [InlineData(0.74, Rarity.Common)]
    [InlineData(1.0, Rarity.Uncommon)]
    [InlineData(1.6, Rarity.Rare)]
    [InlineData(2.2, Rarity.Epic)]
    [InlineData(2.6, Rarity.Legendary)]
    [InlineData(2.95, Rarity.Legendary)]
    public void RarityForPower_BandsTheMultiplier(double multiplier, Rarity expected)
    {
        Assert.Equal(expected, RarityExtensions.ForPower(multiplier));
    }

    [Fact]
    public void RepresentativeMultiplier_RoundTripsThroughForPower()
    {
        foreach (var rarity in System.Enum.GetValues<Rarity>())
        {
            Assert.Equal(rarity, RarityExtensions.ForPower(LootScaling.RepresentativeMultiplier(rarity)));
        }
    }

    [Fact]
    public void DropWeight_FallsAsRarityRises()
    {
        Assert.True(Rarity.Common.DropWeight() > Rarity.Uncommon.DropWeight());
        Assert.True(Rarity.Uncommon.DropWeight() > Rarity.Rare.DropWeight());
        Assert.True(Rarity.Rare.DropWeight() > Rarity.Epic.DropWeight());
        Assert.True(Rarity.Epic.DropWeight() > Rarity.Legendary.DropWeight());
        Assert.True(Rarity.Common.DropWeight() > Rarity.Legendary.DropWeight() * 20);
    }
}
