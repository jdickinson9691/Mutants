using ChronTravelers.Core.Economy;
using ChronTravelers.Core.Items;

namespace ChronTravelers.Core.Tests.Economy;

public class EconomyPricingTests
{
    [Fact]
    public void BuyPrice_IsLessThanItemValue()
    {
        var item = Item.Create("Scrap", ItemType.Junk, tier: 3, Rarity.Common);
        Assert.True(EconomyPricing.BuyPrice(item) < item.Value);
    }

    [Fact]
    public void DefaultAskingPrice_IsMoreThanItemValue()
    {
        var item = Item.Create("Scrap", ItemType.Junk, tier: 3, Rarity.Common);
        Assert.True(EconomyPricing.DefaultAskingPrice(item) > item.Value);
    }

    [Fact]
    public void BuyPrice_NeverGoesBelowOne()
    {
        var item = Item.Create("Trivial", ItemType.Junk, tier: 1, Rarity.Common);
        Assert.True(EconomyPricing.BuyPrice(item) >= 1);
    }

    [Fact]
    public void Margins_CreateARiblettSinkAcrossABuyThenResellCycle()
    {
        // A store buying then reselling the same item should net a
        // surplus for the store (a Riblet sink from the seller's
        // perspective) - docs/GDD.md §6.3.
        var item = Item.Create("Scrap", ItemType.Junk, tier: 3, Rarity.Common);
        Assert.True(EconomyPricing.DefaultAskingPrice(item) > EconomyPricing.BuyPrice(item));
    }
}
