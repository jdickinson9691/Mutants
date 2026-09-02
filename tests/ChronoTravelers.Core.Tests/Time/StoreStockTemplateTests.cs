using ChronoTravelers.Core.Time;

namespace ChronoTravelers.Core.Tests.Time;

public class StoreStockTemplateTests
{
    [Fact]
    public void Default_OffersThreePurchasableSlots()
    {
        Assert.Equal(3, StoreStockTemplate.Default.PlayerSlotCount);
    }

    [Fact]
    public void PlayerSlotCostForTier_ScalesLinearlyAboveTheBase()
    {
        var template = new StoreStockTemplate(PlayerSlotBaseCost: 100, PlayerSlotCostPerTier: 50);

        Assert.Equal(100, template.PlayerSlotCostForTier(1));
        Assert.Equal(150, template.PlayerSlotCostForTier(2));
        Assert.Equal(300, template.PlayerSlotCostForTier(5));
    }

    [Fact]
    public void PlayerSlotCostForTier_NeverGoesBelowTheBaseForATierUnderOne()
    {
        var template = new StoreStockTemplate(PlayerSlotBaseCost: 100, PlayerSlotCostPerTier: 50);

        Assert.Equal(100, template.PlayerSlotCostForTier(0));
    }

    [Fact]
    public void PlayerSlotCount_DefaultsToThreeWhenOmitted()
    {
        var template = new StoreStockTemplate(PlayerSlotBaseCost: 100, PlayerSlotCostPerTier: 50);

        Assert.Equal(3, template.PlayerSlotCount);
    }

    [Fact]
    public void PlayerSlotCount_IsContentAuthorable()
    {
        var template = new StoreStockTemplate(PlayerSlotBaseCost: 100, PlayerSlotCostPerTier: 50, PlayerSlotCount: 7);

        Assert.Equal(7, template.PlayerSlotCount);
    }
}
