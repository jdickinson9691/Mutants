using Mutants.Core.Items;

namespace Mutants.Core.Tests.Items;

public class LootScalingTests
{
    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(10, 100)]
    public void TierBaseValue_IsTenTimesTier(int tier, int expected)
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
        Assert.Equal(10, LootScaling.ValueFor(tier: 1, Rarity.Common));
        Assert.Equal(20, LootScaling.ValueFor(tier: 1, Rarity.Legendary));
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
        Assert.Equal(25.0, LootScaling.TierBaseValue(2.5), precision: 6);

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
}
