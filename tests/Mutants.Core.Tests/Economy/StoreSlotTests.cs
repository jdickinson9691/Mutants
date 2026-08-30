using Mutants.Core.Characters;
using Mutants.Core.Classes;
using Mutants.Core.Economy;
using Mutants.Core.World;

namespace Mutants.Core.Tests.Economy;

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
    public void Purchase_SpendsRibletsAndCreatesAnOwnedStore()
    {
        var slot = new StoreSlot("Gutted Storefront", Coordinate.Origin, homeLevel: 1, purchaseCost: 150);
        var buyer = new Mutant("Rook", CharacterClass.Warrior);
        buyer.AddRiblets(200);

        var store = slot.Purchase(buyer);

        Assert.Equal(50, buyer.Riblets);
        Assert.Same(store, slot.Store);
        Assert.Equal(buyer, store.Owner);
        Assert.False(slot.IsAvailableForPurchase);
    }

    [Fact]
    public void Purchase_ThrowsIfAlreadyOccupied()
    {
        var slot = new StoreSlot("Gutted Storefront", Coordinate.Origin, homeLevel: 1, purchaseCost: 150);
        var firstBuyer = new Mutant("Rook", CharacterClass.Warrior);
        firstBuyer.AddRiblets(200);
        slot.Purchase(firstBuyer);

        var secondBuyer = new Mutant("Zeta", CharacterClass.Mage);
        secondBuyer.AddRiblets(200);

        Assert.Throws<InvalidOperationException>(() => slot.Purchase(secondBuyer));
    }

    [Fact]
    public void Purchase_ThrowsIfBuyerCannotAffordIt()
    {
        var slot = new StoreSlot("Gutted Storefront", Coordinate.Origin, homeLevel: 1, purchaseCost: 150);
        var buyer = new Mutant("Rook", CharacterClass.Warrior); // 0 Riblets

        Assert.Throws<InvalidOperationException>(() => slot.Purchase(buyer));
    }
}
