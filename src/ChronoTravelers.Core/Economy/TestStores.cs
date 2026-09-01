using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.World;

namespace ChronoTravelers.Core.Economy;

/// <summary>
/// Sandbox store slots for the test level, mirroring World.TestLevel /
/// Monsters.TestMonsters. NOT launch content — real store catalogs and
/// placement are future Content Agent work per docs/CONTENT_PLAN.md.
/// Placed in existing TestLevel rooms that already fit the theme: the
/// "maintenance shop" and "gutted storefront" rooms.
/// </summary>
public static class TestStores
{
    public static IReadOnlyList<StoreSlot> Build()
    {
        var rationDepot = Store.CreateGovernmentStore("Ration Depot", homeLevel: 1);
        StockStarterGoods(rationDepot,
        [
            Item.Create("Stale Ration Pack", ItemType.Consumable, 1, Rarity.Common),
            Item.Create("Patch Kit", ItemType.Consumable, 1, Rarity.Common),
            Item.Create("Cracked Shiv", ItemType.Weapon, 1, Rarity.Common),
        ]);

        var streetExchange = Store.CreateGovernmentStore("Streetside Exchange", homeLevel: 1);
        StockStarterGoods(streetExchange,
        [
            Item.Create("Salvaged Plating", ItemType.Armor, 1, Rarity.Common),
            Item.Create("Pipe Wrench", ItemType.Weapon, 1, Rarity.Common),
        ]);

        return
        [
            new StoreSlot("Ration Depot", new Coordinate(1, 0), homeLevel: 1, purchaseCost: 0, rationDepot),
            new StoreSlot("Streetside Exchange", new Coordinate(0, -1), homeLevel: 1, purchaseCost: 0, streetExchange),
            new StoreSlot("Gutted Storefront", new Coordinate(1, 1), homeLevel: 1, purchaseCost: 150, store: null),
        ];
    }

    private static void StockStarterGoods(Store store, IEnumerable<Item> items)
    {
        foreach (var item in items)
        {
            store.Stock(item, EconomyPricing.DefaultAskingPrice(item));
        }
    }
}
