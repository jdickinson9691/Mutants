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

    [Fact]
    public void DepositCredits_RequiresOwnership()
    {
        var owner = NewTraveler("Owner");
        var stranger = NewTraveler("Stranger");
        stranger.AddCredits(50);
        var store = new Store("Owner's Store", homeLevel: 1, startingCapital: 100, owner);

        Assert.Throws<InvalidOperationException>(() => store.Deposit(stranger, 10));
    }

    [Fact]
    public void DepositCredits_MovesOwnerCreditsIntoStoreCapital()
    {
        var owner = NewTraveler("Owner");
        owner.AddCredits(50);
        var store = new Store("Owner's Store", homeLevel: 1, startingCapital: 100, owner);

        store.Deposit(owner, 30);

        Assert.Equal(20, owner.Credits);
        Assert.Equal(130, store.Capital);
    }

    [Fact]
    public void DepositCredits_RejectsANonPositiveAmount()
    {
        var owner = NewTraveler("Owner");
        var store = new Store("Owner's Store", homeLevel: 1, startingCapital: 100, owner);

        Assert.Throws<ArgumentOutOfRangeException>(() => store.Deposit(owner, 0));
    }

    [Fact]
    public void Charge_RequiresOwnership()
    {
        var owner = NewTraveler("Owner");
        var stranger = NewTraveler("Stranger");
        var store = new Store("Owner's Store", homeLevel: 1, startingCapital: 100, owner);

        Assert.Throws<InvalidOperationException>(() => store.Charge(stranger, 10));
    }

    [Fact]
    public void Charge_MovesOwnerTachyonsIntoTheMaintenanceReserve()
    {
        var owner = NewTraveler("Owner");
        var tachyonsBefore = owner.Tachyons.Current;
        var store = new Store("Owner's Store", homeLevel: 1, startingCapital: 100, owner);

        store.Charge(owner, 5);

        Assert.Equal(tachyonsBefore - 5, owner.Tachyons.Current);
        Assert.Equal(5, store.TachyonReserve);
    }

    [Fact]
    public void Charge_ThrowsIfOwnerCantAffordIt()
    {
        var owner = NewTraveler("Owner");
        owner.Tachyons.Spend(owner.Tachyons.Current);
        var store = new Store("Owner's Store", homeLevel: 1, startingCapital: 100, owner);

        Assert.Throws<InvalidOperationException>(() => store.Charge(owner, 1));
    }

    [Fact]
    public void ApplyMaintenanceTick_GovernmentStore_IsAlwaysANoOp()
    {
        var store = Store.CreateGovernmentStore("Ration Depot", homeLevel: 1);

        var foreclosed = store.ApplyMaintenanceTick(1_000_000);

        Assert.False(foreclosed);
        Assert.Equal(0, store.MissedMaintenanceTicks);
    }

    [Fact]
    public void ApplyMaintenanceTick_SufficientReserve_DrawsTheCostAndResetsTheMissStreak()
    {
        var owner = NewTraveler("Owner");
        var store = new Store("Owner's Store", homeLevel: 1, startingCapital: 100, owner, startingTachyonReserve: 10);
        store.ApplyMaintenanceTick(3); // one miss first, to prove a paid tick resets it
        var underfunded = new Store("Underfunded", homeLevel: 1, startingCapital: 0, owner, startingTachyonReserve: 0);
        underfunded.ApplyMaintenanceTick(1);
        Assert.Equal(1, underfunded.MissedMaintenanceTicks);

        var foreclosed = store.ApplyMaintenanceTick(3);

        Assert.False(foreclosed);
        Assert.Equal(4, store.TachyonReserve);
        Assert.Equal(0, store.MissedMaintenanceTicks);
    }

    [Fact]
    public void ApplyMaintenanceTick_InsufficientReserve_DrainsItAndRecordsAMiss()
    {
        var owner = NewTraveler("Owner");
        var store = new Store("Owner's Store", homeLevel: 1, startingCapital: 100, owner, startingTachyonReserve: 2);

        var foreclosed = store.ApplyMaintenanceTick(5);

        Assert.False(foreclosed);
        Assert.Equal(0, store.TachyonReserve);
        Assert.Equal(1, store.MissedMaintenanceTicks);
    }

    [Fact]
    public void ApplyMaintenanceTick_ReachingTheForeclosureThreshold_ReturnsTrue()
    {
        var owner = NewTraveler("Owner");
        var store = new Store("Owner's Store", homeLevel: 1, startingCapital: 100, owner, startingTachyonReserve: 0);

        var foreclosed = false;
        for (var i = 0; i < Store.ForeclosureThreshold; i++)
        {
            foreclosed = store.ApplyMaintenanceTick(1);
        }

        Assert.True(foreclosed);
        Assert.Equal(Store.ForeclosureThreshold, store.MissedMaintenanceTicks);
    }

    private static Store FullStore(string name = "Packed Store")
    {
        var store = Store.CreateGovernmentStore(name, homeLevel: 1);
        for (var i = 0; i < Store.MaxListings; i++)
        {
            store.Stock(Item.Create($"Filler {i}", ItemType.Junk, 1, Rarity.Common), askingPrice: 1);
        }

        return store;
    }

    [Fact]
    public void Stock_AtMaxListings_ReturnsFalseAndAddsNothing()
    {
        var store = FullStore();

        var added = store.Stock(Item.Create("One Too Many", ItemType.Junk, 1, Rarity.Common), askingPrice: 5);

        Assert.False(added);
        Assert.Equal(Store.MaxListings, store.Listings.Count);
    }

    [Fact]
    public void ClearOldestListing_RemovesAndReturnsTheFirstListing_LeavingCapitalUntouched()
    {
        var store = Store.CreateGovernmentStore("Depot", homeLevel: 1);
        var oldest = Item.Create("Stale Blade", ItemType.Weapon, 1, Rarity.Common);
        store.Stock(oldest, askingPrice: 10);
        store.Stock(Item.Create("Newer Blade", ItemType.Weapon, 1, Rarity.Common), askingPrice: 12);
        var capitalBefore = store.Capital;

        var cleared = store.ClearOldestListing();

        Assert.Equal(oldest, cleared);
        Assert.Single(store.Listings);
        Assert.DoesNotContain(store.Listings, l => l.Item == oldest);
        Assert.Equal(capitalBefore, store.Capital);
    }

    [Fact]
    public void ClearOldestListing_OnAnEmptyShelf_ReturnsNull()
    {
        var store = Store.CreateGovernmentStore("Depot", homeLevel: 1);
        while (store.Listings.Count > 0)
        {
            store.ClearOldestListing();
        }

        Assert.Null(store.ClearOldestListing());
    }

    [Fact]
    public void Stock_AtMaxListings_WithCapDisabled_StillAdds()
    {
        // The Persistence layer's escape hatch (ChronoTravelers.Engine.Persistence
        // .CharacterMapper.ApplyOwnedStores) for a save written before this
        // cap existed — a returning owner's stock must never be dropped.
        var store = FullStore();

        var added = store.Stock(Item.Create("Grandfathered", ItemType.Junk, 1, Rarity.Common), askingPrice: 5, enforceCap: false);

        Assert.True(added);
        Assert.Equal(Store.MaxListings + 1, store.Listings.Count);
    }

    [Fact]
    public void BuyFromTraveler_StoreAtMaxListings_ReturnsNullAndDoesNothing()
    {
        var store = FullStore();
        var seller = NewTraveler();
        var item = Item.Create("Scrap", ItemType.Junk, 2, Rarity.Common);
        seller.AddToInventory(item);

        var price = store.BuyFromTraveler(seller, item);

        Assert.Null(price);
        Assert.Equal(0, seller.Credits);
        Assert.Contains(item, seller.Inventory);
        Assert.Equal(Store.MaxListings, store.Listings.Count);
    }

    [Fact]
    public void Deposit_StoreAtMaxListings_ReturnsFalseAndLeavesItemWithOwner()
    {
        var owner = NewTraveler("Owner");
        var store = new Store("Owner's Store", homeLevel: 1, startingCapital: 100, owner);
        for (var i = 0; i < Store.MaxListings; i++)
        {
            store.Stock(Item.Create($"Filler {i}", ItemType.Junk, 1, Rarity.Common), askingPrice: 1);
        }

        var item = Item.Create("Widget", ItemType.Junk, 1, Rarity.Common);
        owner.AddToInventory(item);

        var deposited = store.Deposit(owner, item, askingPrice: 10);

        Assert.False(deposited);
        Assert.Contains(item, owner.Inventory);
        Assert.Equal(Store.MaxListings, store.Listings.Count);
    }

    [Fact]
    public void SellToTraveler_BuyerPackIsFull_ReturnsFalseAndSpendsNothing()
    {
        var store = Store.CreateGovernmentStore("Ration Depot", homeLevel: 1);
        var item = Item.Create("Patch Kit", ItemType.Consumable, 1, Rarity.Common);
        store.Stock(item, askingPrice: 15);
        var buyer = NewTraveler();
        buyer.AddCredits(20);
        for (var i = 0; i < Traveler.MaxInventorySize; i++)
        {
            buyer.AddToInventory(Item.Create($"Junk {i}", ItemType.Junk, 1, Rarity.Common));
        }

        var bought = store.SellToTraveler(buyer, store.Listings[0]);

        Assert.False(bought);
        Assert.Equal(20, buyer.Credits);
        Assert.DoesNotContain(item, buyer.Inventory);
        Assert.Single(store.Listings);
    }

    [Fact]
    public void Withdraw_OwnerPackIsFull_ReturnsFalseAndLeavesItemListed()
    {
        var owner = NewTraveler("Owner");
        var store = new Store("Owner's Store", homeLevel: 1, startingCapital: 100, owner);
        var item = Item.Create("Widget", ItemType.Junk, 1, Rarity.Common);
        store.Stock(item, 10);
        for (var i = 0; i < Traveler.MaxInventorySize; i++)
        {
            owner.AddToInventory(Item.Create($"Junk {i}", ItemType.Junk, 1, Rarity.Common));
        }

        var withdrawn = store.Withdraw(owner, store.Listings[0]);

        Assert.False(withdrawn);
        Assert.DoesNotContain(item, owner.Inventory);
        Assert.Single(store.Listings);
    }
}
