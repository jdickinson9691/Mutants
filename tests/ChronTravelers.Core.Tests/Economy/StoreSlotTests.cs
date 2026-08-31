using ChronTravelers.Core.Characters;
using ChronTravelers.Core.Classes;
using ChronTravelers.Core.Economy;
using ChronTravelers.Core.World;

namespace ChronTravelers.Core.Tests.Economy;

public class StoreSlotTests
{
    [Fact]
    public void EmptySlot_IsAvailableForPurchase()
    {
        var slot = new StoreSlot("Gutted Storefront", Coordinate.Origin, homeLevel: 1, purchaseCost: 150);
        Assert.True(slot.IsAvailableForPurchase);
        Assert.Null(slot.Store);
    }

    [Fact]
    public void PreSeededSlot_IsNotAvailableForPurchase()
    {
        var store = Store.CreateGovernmentStore("Ration Depot", homeLevel: 1);
        var slot = new StoreSlot("Ration Depot", Coordinate.Origin, homeLevel: 1, purchaseCost: 0, store);

        Assert.False(slot.IsAvailableForPurchase);
    }

    [Fact]
    public void Purchase_SpendsCreditsAndCreatesAnOwnedStore()
    {
        var slot = new StoreSlot("Gutted Storefront", Coordinate.Origin, homeLevel: 1, purchaseCost: 150);
        var buyer = new Traveler("Rook", CharacterClass.Soldier);
        buyer.AddCredits(200);

        var store = slot.Purchase(buyer);

        Assert.Equal(50, buyer.Credits);
        Assert.Same(store, slot.Store);
        Assert.Equal(buyer, store.Owner);
        Assert.False(slot.IsAvailableForPurchase);
    }

    [Fact]
    public void Purchase_ThrowsIfAlreadyOccupied()
    {
        var slot = new StoreSlot("Gutted Storefront", Coordinate.Origin, homeLevel: 1, purchaseCost: 150);
        var firstBuyer = new Traveler("Rook", CharacterClass.Soldier);
        firstBuyer.AddCredits(200);
        slot.Purchase(firstBuyer);

        var secondBuyer = new Traveler("Zeta", CharacterClass.Scientist);
        secondBuyer.AddCredits(200);

        Assert.Throws<InvalidOperationException>(() => slot.Purchase(secondBuyer));
    }

    [Fact]
    public void Purchase_ThrowsIfBuyerCannotAffordIt()
    {
        var slot = new StoreSlot("Gutted Storefront", Coordinate.Origin, homeLevel: 1, purchaseCost: 150);
        var buyer = new Traveler("Rook", CharacterClass.Soldier); // 0 Credits

        Assert.Throws<InvalidOperationException>(() => slot.Purchase(buyer));
    }

    [Fact]
    public void RestoreOwnership_ReattachesAStoreWithNoCreditChargeAndKeepsTheCapital()
    {
        var slot = new StoreSlot("Vacant Storefront", Coordinate.Origin, homeLevel: 2600, purchaseCost: 400);
        var owner = new Traveler("Rook", CharacterClass.Soldier); // 0 Credits — Purchase would throw

        var store = slot.RestoreOwnership(owner, capital: 375);

        Assert.Same(store, slot.Store);
        Assert.Equal(owner, store.Owner);
        Assert.Equal(375, store.Capital);
        Assert.Equal(0, owner.Credits); // never charged
        Assert.False(slot.IsAvailableForPurchase);
    }

    [Fact]
    public void RestoreOwnership_ThrowsIfAlreadyOccupied()
    {
        var store = Store.CreateGovernmentStore("Depot", homeLevel: 2600);
        var slot = new StoreSlot("Depot", Coordinate.Origin, homeLevel: 2600, purchaseCost: 0, store);

        Assert.Throws<InvalidOperationException>(() => slot.RestoreOwnership(new Traveler("Rook", CharacterClass.Soldier), capital: 0));
    }
}
