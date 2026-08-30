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
}
