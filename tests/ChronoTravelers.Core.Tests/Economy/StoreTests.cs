using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Economy;
using ChronoTravelers.Core.Items;

namespace ChronoTravelers.Core.Tests.Economy;

public class StoreTests
{
    private static Traveler NewTraveler(string name = "Rook") => new(name, CharacterClass.Soldier);

    [Fact]
    public void CreateGovernmentStore_HasNoOwnerAndHugeCapital()
    {
        var store = Store.CreateGovernmentStore("Ration Depot", homeLevel: 1);

        Assert.True(store.IsGovernmentRun);
        Assert.Null(store.Owner);
        Assert.True(store.Capital > 1000);
    }

    [Fact]
    public void Stock_AddsListingWithoutTouchingAnyTraveler()
    {
        var store = Store.CreateGovernmentStore("Ration Depot", homeLevel: 1);
        var item = Item.Create("Patch Kit", ItemType.Consumable, 1, Rarity.Common);

        store.Stock(item, askingPrice: 15);

        Assert.Single(store.Listings);
        Assert.Equal(15, store.Listings[0].AskingPrice);
    }

    [Fact]
    public void BuyFromTraveler_PaysSellerAndRelistsTheItem()
    {
        var store = Store.CreateGovernmentStore("Ration Depot", homeLevel: 1);
        var seller = NewTraveler();
        var item = Item.Create("Scrap", ItemType.Junk, tier: 2, Rarity.Common); // value 20
        seller.AddToInventory(item);

        var price = store.BuyFromTraveler(seller, item);

        Assert.NotNull(price);
        Assert.Equal(price, seller.Credits);
        Assert.DoesNotContain(item, seller.Inventory);
        Assert.Contains(store.Listings, l => l.Item == item);
    }

    [Fact]
    public void BuyFromTraveler_ReturnsNullAndDoesNothingWhenStoreCantAfford()
    {
        var store = new Store("Corner Shop", homeLevel: 1, startingCapital: 0);
        var seller = NewTraveler();
        var item = Item.Create("Scrap", ItemType.Junk, 2, Rarity.Common);
        seller.AddToInventory(item);

        var price = store.BuyFromTraveler(seller, item);

        Assert.Null(price);
        Assert.Equal(0, seller.Credits);
        Assert.Contains(item, seller.Inventory);
        Assert.Empty(store.Listings);
    }

    [Fact]
    public void SellToTraveler_TransfersItemAndChargesCredits()
    {
        var store = Store.CreateGovernmentStore("Ration Depot", homeLevel: 1);
        var item = Item.Create("Patch Kit", ItemType.Consumable, 1, Rarity.Common);
        store.Stock(item, askingPrice: 15);
        var buyer = NewTraveler();
        buyer.AddCredits(20);
        var capitalBefore = store.Capital;

        store.SellToTraveler(buyer, store.Listings[0]);

        Assert.Equal(5, buyer.Credits);
        Assert.Contains(item, buyer.Inventory);
        Assert.Empty(store.Listings);
        Assert.Equal(capitalBefore + 15, store.Capital);
    }

    [Fact]
    public void SellToTraveler_ThrowsForAListingNotAtThisStore()
    {
        var store = Store.CreateGovernmentStore("Ration Depot", homeLevel: 1);
        var phantomListing = new StoreListing(Item.Create("Ghost", ItemType.Junk, 1, Rarity.Common), 5);
        var buyer = NewTraveler();
        buyer.AddCredits(10);

        Assert.Throws<InvalidOperationException>(() => store.SellToTraveler(buyer, phantomListing));
    }

    [Fact]
    public void Deposit_RequiresOwnership()
    {
        var owner = NewTraveler("Owner");
        var stranger = NewTraveler("Stranger");
        var store = new Store("Owner's Store", homeLevel: 1, startingCapital: 100, owner);
        var item = Item.Create("Widget", ItemType.Junk, 1, Rarity.Common);
        stranger.AddToInventory(item);

        Assert.Throws<InvalidOperationException>(() => store.Deposit(stranger, item, askingPrice: 10));
    }

    [Fact]
    public void Deposit_MovesItemFromOwnerInventoryIntoAListing()
    {
        var owner = NewTraveler("Owner");
        var store = new Store("Owner's Store", homeLevel: 1, startingCapital: 100, owner);
        var item = Item.Create("Widget", ItemType.Junk, 1, Rarity.Common);
        owner.AddToInventory(item);

        store.Deposit(owner, item, askingPrice: 10);

        Assert.DoesNotContain(item, owner.Inventory);
        Assert.Contains(store.Listings, l => l.Item == item && l.AskingPrice == 10);
    }

    [Fact]
    public void Withdraw_RequiresOwnership()
    {
        var owner = NewTraveler("Owner");
        var stranger = NewTraveler("Stranger");
        var store = new Store("Owner's Store", homeLevel: 1, startingCapital: 100, owner);
        var item = Item.Create("Widget", ItemType.Junk, 1, Rarity.Common);
        store.Stock(item, 10);

        Assert.Throws<InvalidOperationException>(() => store.Withdraw(stranger, store.Listings[0]));
    }

    [Fact]
    public void Withdraw_ReturnsItemToOwnerInventoryAndUnlists()
    {
        var owner = NewTraveler("Owner");
        var store = new Store("Owner's Store", homeLevel: 1, startingCapital: 100, owner);
        var item = Item.Create("Widget", ItemType.Junk, 1, Rarity.Common);
        store.Stock(item, 10);

        store.Withdraw(owner, store.Listings[0]);

        Assert.Contains(item, owner.Inventory);
        Assert.Empty(store.Listings);
    }

    [Fact]
    public void AdjustPrice_RequiresOwnership()
    {
        var owner = NewTraveler("Owner");
        var stranger = NewTraveler("Stranger");
        var store = new Store("Owner's Store", homeLevel: 1, startingCapital: 100, owner);
        store.Stock(Item.Create("Widget", ItemType.Junk, 1, Rarity.Common), 10);

        Assert.Throws<InvalidOperationException>(() => store.AdjustPrice(stranger, store.Listings[0], 20));
    }

    [Fact]
    public void AdjustPrice_ChangesTheAskingPrice()
    {
        var owner = NewTraveler("Owner");
        var store = new Store("Owner's Store", homeLevel: 1, startingCapital: 100, owner);
        store.Stock(Item.Create("Widget", ItemType.Junk, 1, Rarity.Common), 10);

        store.AdjustPrice(owner, store.Listings[0], 25);

        Assert.Equal(25, store.Listings[0].AskingPrice);
    }

    [Fact]
    public void CollectCapital_RequiresOwnership()
    {
        var owner = NewTraveler("Owner");
        var stranger = NewTraveler("Stranger");
        var store = new Store("Owner's Store", homeLevel: 1, startingCapital: 100, owner);

        Assert.Throws<InvalidOperationException>(() => store.CollectCapital(stranger, 10));
    }

    [Fact]
    public void CollectCapital_MovesStoreCapitalToOwnerCredits()
    {
        var owner = NewTraveler("Owner");
        var store = new Store("Owner's Store", homeLevel: 1, startingCapital: 100, owner);

        var collected = store.CollectCapital(owner, 40);

        Assert.Equal(40, collected);
        Assert.Equal(40, owner.Credits);
        Assert.Equal(60, store.Capital);
    }

    [Fact]
    public void CollectCapital_RejectsAmountBeyondAvailableCapital()
    {
        var owner = NewTraveler("Owner");
        var store = new Store("Owner's Store", homeLevel: 1, startingCapital: 100, owner);

        Assert.Throws<ArgumentOutOfRangeException>(() => store.CollectCapital(owner, 101));
    }

    [Fact]
    public void GovernmentStore_CanNeverBeOwnedOrCollectedFrom()
    {
        var owner = NewTraveler("Owner");
        var store = Store.CreateGovernmentStore("Ration Depot", homeLevel: 1);

        Assert.Throws<InvalidOperationException>(() => store.CollectCapital(owner, 1));
    }
}
