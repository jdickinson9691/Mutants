using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Economy;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.World;

namespace ChronoTravelers.Core.Tests.Economy;

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

    [Fact]
    public void RestoreOwnership_RestoresTheCreditReserveToo()
    {
        var slot = new StoreSlot("Vacant Storefront", Coordinate.Origin, homeLevel: 2600, purchaseCost: 400);
        var owner = new Traveler("Rook", CharacterClass.Soldier);

        var store = slot.RestoreOwnership(owner, capital: 0, creditReserve: 42);

        Assert.Equal(42, store.CreditReserve);
    }

    [Fact]
    public void Repossess_ThrowsOnAnAlreadyVacantSlot()
    {
        var slot = new StoreSlot("Gutted Storefront", Coordinate.Origin, homeLevel: 1, purchaseCost: 150);

        Assert.Throws<InvalidOperationException>(() => slot.Repossess());
    }

    [Fact]
    public void Repossess_ThrowsOnAGovernmentStore()
    {
        var store = Store.CreateGovernmentStore("Depot", homeLevel: 1);
        var slot = new StoreSlot("Depot", Coordinate.Origin, homeLevel: 1, purchaseCost: 0, store);

        Assert.Throws<InvalidOperationException>(() => slot.Repossess());
    }

    [Fact]
    public void Repossess_ClearsTheOwnerAndMakesTheSlotAvailableAgain()
    {
        var slot = new StoreSlot("Gutted Storefront", Coordinate.Origin, homeLevel: 1, purchaseCost: 150);
        var buyer = new Traveler("Rook", CharacterClass.Soldier);
        buyer.AddCredits(200);
        slot.Purchase(buyer);

        slot.Repossess();

        Assert.True(slot.IsAvailableForPurchase);
        Assert.Null(slot.Store);
    }

    [Fact]
    public void Repossess_PreservesListingsForWhoeverBuysTheSlotNext()
    {
        var slot = new StoreSlot("Gutted Storefront", Coordinate.Origin, homeLevel: 1, purchaseCost: 150);
        var firstOwner = new Traveler("Rook", CharacterClass.Soldier);
        firstOwner.AddCredits(200);
        var store = slot.Purchase(firstOwner);
        var item = Item.Create("Widget", ItemType.Junk, 1, Rarity.Common);
        store.Stock(item, askingPrice: 25);

        slot.Repossess();
        Assert.True(slot.HasAbandonedInventory);

        var secondOwner = new Traveler("Zeta", CharacterClass.Scientist);
        secondOwner.AddCredits(200);
        var newStore = slot.Purchase(secondOwner);

        Assert.Contains(newStore.Listings, l => l.Item == item && l.AskingPrice == 25);
        Assert.False(slot.HasAbandonedInventory);
    }
}
