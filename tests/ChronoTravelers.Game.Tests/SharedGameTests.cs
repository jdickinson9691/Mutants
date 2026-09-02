using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Monsters;
using ChronoTravelers.Core.Time;
using ChronoTravelers.Engine;
using ChronoTravelers.Game;

namespace ChronoTravelers.Game.Tests;

public class SharedGameTests
{
    private sealed class Recorder : IGameOutput
    {
        public List<string> Lines { get; } = [];
        public void Line(string text) => Lines.Add(text);
        public bool Any(string needle) => Lines.Any(l => l.Contains(needle, StringComparison.OrdinalIgnoreCase));
        public void Clear() => Lines.Clear();
    }

    private static SharedGame NewGame(out TimeWorld world)
    {
        world = TestTimeWorld.Build(seed: 4242);
        return new SharedGame(world, Array.Empty<Traveler>(), new SystemRandomSource());
    }

    private static Traveler NewSoldier(string name = "Rook") => new(name, CharacterClass.Soldier);

    [Fact]
    public void Join_PlacesThePlayerAtTheStartRoom_AndDescribesIt()
    {
        var game = NewGame(out var world);
        var rec = new Recorder();
        var player = NewSoldier();
        player.PlaceAt(new Core.World.Coordinate(999, 999)); // off-map

        var session = game.Join("acct", player, rec);

        Assert.Equal(world.GetYear(player.CurrentYear).Map.Start, player.Position);
        Assert.True(rec.Any("2000 A.D."));
        Assert.Equal(1, game.OnlineCount);
        Assert.Equal("acct", session.AccountName);
    }

    [Fact]
    public void Join_AnnouncesToTheOthersAlreadyOnline()
    {
        var game = NewGame(out _);
        var a = new Recorder();
        game.Join("a", NewSoldier("Ada"), a);
        a.Clear();

        game.Join("b", NewSoldier("Bo"), new Recorder());

        Assert.True(a.Any("Bo") && a.Any("surfaced"));
    }

    [Fact]
    public void Move_ChangesPositionAndRedescribesTheRoom()
    {
        var game = NewGame(out var world);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);
        var start = session.Player.Position;

        // Pick a real exit from the start room.
        var exit = world.GetYear(session.Player.CurrentYear).Map.GetRoom(start).ExitDescriptions.Keys.First();
        rec.Clear();
        game.Execute(session, exit.ToString());

        Assert.NotEqual(start, session.Player.Position);
        Assert.True(rec.Any("Exits:"));
    }

    [Fact]
    public void Say_ReachesEveryConnectedSession()
    {
        var game = NewGame(out _);
        var a = new Recorder();
        var b = new Recorder();
        var sa = game.Join("a", NewSoldier("Ada"), a);
        game.Join("b", NewSoldier("Bo"), b);
        b.Clear();

        game.Execute(sa, "say hello timeline");

        Assert.True(b.Any("Ada says: hello timeline"));
    }

    [Fact]
    public void Tick_RunsTachyonBookkeepingForEveryConnectedPlayer()
    {
        // The deep future net-drains Tachyons (GDD §2.1) — a good check that the
        // per-tick loop touches every connected player, not just one.
        var game = NewGame(out _);
        var p1 = NewSoldier("A");
        var p2 = NewSoldier("B");
        game.Join("a", p1, new Recorder());
        game.Join("b", p2, new Recorder());
        p1.SetCurrentYear(5000);
        p2.SetCurrentYear(5000);

        var before1 = p1.Tachyons.Current;
        var before2 = p2.Tachyons.Current;

        for (var i = 0; i < 120; i++)
        {
            game.Tick();
        }

        Assert.True(p1.Tachyons.Current < before1, "player 1 Tachyons should have net-drained in 5000 A.D.");
        Assert.True(p2.Tachyons.Current < before2, "player 2 Tachyons should have net-drained in 5000 A.D.");
    }

    [Fact]
    public void Fight_ResolvesAndDropsLootOnTheFloor_NotIntoThePack()
    {
        var game = NewGame(out var world);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);
        var packBefore = session.Player.Inventory.Count;

        var pop = world.GetYear(session.Player.CurrentYear).Population;
        var trophy = new LootTableEntry(Item.Create("Test Fang", ItemType.Junk, 1, Rarity.Common), dropChance: 1.0);
        var weakling = new Monster("Weakling", 1, maxHp: 1, attackPower: 0, defense: 0, speed: 1, xpReward: 10, lootTable: [trophy]);
        weakling.PlaceAt(session.Player.Position);
        pop.AddMonster(weakling);

        rec.Clear();
        game.Execute(session, "fight");

        Assert.True(rec.Any("defeated the Weakling"));
        Assert.Equal(packBefore, session.Player.Inventory.Count);                       // nothing auto-acquired
        Assert.Contains(pop.LootAt(session.Player.Position), i => i.Name == "Test Fang"); // it's on the floor
    }

    [Fact]
    public void Travel_DeductsTachyonsAndMovesTheYear()
    {
        var game = NewGame(out _);
        var session = game.Join("a", NewSoldier(), new Recorder());
        var ionsBefore = session.Player.Tachyons.Current;

        game.Execute(session, "travel +250");

        Assert.Equal(2250, session.Player.CurrentYear);
        Assert.True(session.Player.Tachyons.Current < ionsBefore);
    }

    [Fact]
    public void Wield_PrefersTheWieldableMatch_WhenASubstringHitsSeveralItems()
    {
        var game = NewGame(out _);
        var rec = new Recorder();
        var player = NewSoldier();
        var junk = Item.Create("Salvage Shard", ItemType.Junk, 1, Rarity.Common);
        var shard = Item.Create("Time Shard", ItemType.Weapon, 1, Rarity.Legendary);
        player.AddToInventory(junk);
        player.AddToInventory(shard);
        var session = game.Join("acct", player, rec);
        rec.Clear();

        game.Execute(session, "wield shard");

        Assert.Same(shard, player.EquippedWeapon);
        Assert.True(rec.Any("Wielded Time Shard"));
        Assert.False(rec.Any("can't be wielded"));
    }

    // --- stores (docs/GDD.md §6, docs/SERVER.md command parity) -------------

    [Fact]
    public void Stores_ListsEveryStoreSlotForThePlayersYear()
    {
        var game = NewGame(out var world);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);
        var slotCount = world.GetYear(session.Player.CurrentYear).StoreSlots.Count;

        rec.Clear();
        game.Execute(session, "stores");

        Assert.True(rec.Any($"{slotCount} store slot"));
        Assert.True(rec.Any("government"));
    }

    [Fact]
    public void Shop_AtTheGovernmentStore_ListsItsListings()
    {
        var game = NewGame(out var world);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);
        var govSlot = world.GetYear(session.Player.CurrentYear).StoreSlots.Single(s => s.Store is { IsGovernmentRun: true });
        session.Player.PlaceAt(govSlot.Location);

        rec.Clear();
        game.Execute(session, "shop");

        Assert.True(rec.Any("Capital"));
        Assert.True(rec.Any(govSlot.Store!.Listings[0].Item.Name));
    }

    [Fact]
    public void Shop_AwayFromAnyStore_SaysSo()
    {
        var game = NewGame(out var world);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);
        var storeLocations = world.GetYear(session.Player.CurrentYear).StoreSlots.Select(s => s.Location).ToHashSet();
        var empty = world.GetYear(session.Player.CurrentYear).Map.Rooms.Keys.First(c => !storeLocations.Contains(c));
        session.Player.PlaceAt(empty);

        rec.Clear();
        game.Execute(session, "shop");

        Assert.True(rec.Any("no store here"));
    }

    [Fact]
    public void BuyFromStore_PurchasesAListedItem_DeductingCreditsAndAddingToInventory()
    {
        var game = NewGame(out var world);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);
        var govSlot = world.GetYear(session.Player.CurrentYear).StoreSlots.Single(s => s.Store is { IsGovernmentRun: true });
        session.Player.PlaceAt(govSlot.Location);
        var listing = govSlot.Store!.Listings[0];
        session.Player.AddCredits(listing.AskingPrice + 50);
        var creditsBefore = session.Player.Credits;

        rec.Clear();
        game.Execute(session, $"buy {listing.Item.Name}");

        Assert.Contains(session.Player.Inventory, i => i.Name == listing.Item.Name);
        Assert.Equal(creditsBefore - listing.AskingPrice, session.Player.Credits);
        Assert.True(rec.Any("Bought"));
    }

    [Fact]
    public void BuyFromStore_WithoutEnoughCredits_RefusesAndKeepsTheListing()
    {
        var game = NewGame(out var world);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);
        var govSlot = world.GetYear(session.Player.CurrentYear).StoreSlots.Single(s => s.Store is { IsGovernmentRun: true });
        session.Player.PlaceAt(govSlot.Location);
        var listing = govSlot.Store!.Listings[0];

        rec.Clear();
        game.Execute(session, $"buy {listing.Item.Name}");

        Assert.DoesNotContain(session.Player.Inventory, i => i.Name == listing.Item.Name);
        Assert.True(rec.Any("can't afford"));
    }

    [Fact]
    public void SellToStore_SellsAnItemFromInventoryForCredits()
    {
        var game = NewGame(out var world);
        var rec = new Recorder();
        var player = NewSoldier();
        var junk = Item.Create("Circuit Scrap", ItemType.Junk, 1, Rarity.Common);
        player.AddToInventory(junk);
        var session = game.Join("a", player, rec);
        var govSlot = world.GetYear(session.Player.CurrentYear).StoreSlots.Single(s => s.Store is { IsGovernmentRun: true });
        session.Player.PlaceAt(govSlot.Location);

        rec.Clear();
        game.Execute(session, "sell Circuit Scrap");

        Assert.DoesNotContain(player.Inventory, i => i.Name == "Circuit Scrap");
        Assert.True(player.Credits > 0);
        Assert.True(rec.Any("Sold Circuit Scrap"));
    }

    [Fact]
    public void SellToStore_SellAll_DumpsOnlyJunk()
    {
        var game = NewGame(out var world);
        var rec = new Recorder();
        var player = NewSoldier();
        var junk = Item.Create("Scrap", ItemType.Junk, 1, Rarity.Common);
        var gear = Item.Create("Kept Blade", ItemType.Weapon, 1, Rarity.Common);
        player.AddToInventory(junk);
        player.AddToInventory(gear);
        var session = game.Join("a", player, rec);
        var govSlot = world.GetYear(session.Player.CurrentYear).StoreSlots.Single(s => s.Store is { IsGovernmentRun: true });
        session.Player.PlaceAt(govSlot.Location);

        rec.Clear();
        game.Execute(session, "sell all");

        Assert.DoesNotContain(player.Inventory, i => i.Name == "Scrap");
        Assert.Contains(player.Inventory, i => i.Name == "Kept Blade");
        Assert.True(rec.Any("Sold 1 junk item"));
    }

    [Fact]
    public void BuyStore_PurchasesAVacantSlot()
    {
        var game = NewGame(out var world);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);
        var vacant = world.GetYear(session.Player.CurrentYear).StoreSlots.First(s => s.IsAvailableForPurchase);
        session.Player.PlaceAt(vacant.Location);
        session.Player.AddCredits(vacant.PurchaseCost + 10);

        rec.Clear();
        game.Execute(session, "buy-store");

        Assert.False(vacant.IsAvailableForPurchase);
        Assert.Same(session.Player, vacant.Store!.Owner);
        Assert.True(rec.Any("You now own a store here"));
    }

    [Fact]
    public void StoreManagement_StockDepositChargeWithdrawReprice_AllRequireOwnership()
    {
        var game = NewGame(out var world);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);
        var vacant = world.GetYear(session.Player.CurrentYear).StoreSlots.First(s => s.IsAvailableForPurchase);
        session.Player.PlaceAt(vacant.Location);
        session.Player.AddCredits(vacant.PurchaseCost + 200);
        game.Execute(session, "buy-store");

        var gear = Item.Create("House Blade", ItemType.Weapon, 1, Rarity.Common);
        session.Player.AddToInventory(gear);

        rec.Clear();
        game.Execute(session, "stock House Blade 40");
        Assert.True(rec.Any("Listed House Blade"));
        Assert.Contains(vacant.Store!.Listings, l => l.Item.Name == "House Blade" && l.AskingPrice == 40);

        rec.Clear();
        game.Execute(session, "reprice House Blade 55");
        Assert.True(rec.Any("House Blade is now 55"));
        Assert.Contains(vacant.Store.Listings, l => l.Item.Name == "House Blade" && l.AskingPrice == 55);

        rec.Clear();
        game.Execute(session, "withdraw House Blade");
        Assert.True(rec.Any("Withdrew House Blade"));
        Assert.DoesNotContain(vacant.Store.Listings, l => l.Item.Name == "House Blade");
        Assert.Contains(session.Player.Inventory, i => i.Name == "House Blade");

        var creditsBefore = session.Player.Credits;
        rec.Clear();
        game.Execute(session, "deposit 20");
        Assert.True(rec.Any("Deposited 20"));
        Assert.Equal(creditsBefore - 20, session.Player.Credits);

        var tachyonsBefore = session.Player.Tachyons.Current;
        rec.Clear();
        game.Execute(session, "charge 5");
        Assert.True(rec.Any("Charged 5 Tachyons"));
        Assert.Equal(tachyonsBefore - 5, session.Player.Tachyons.Current);
    }

    [Fact]
    public void StoreManagement_ByANonOwner_IsRefused()
    {
        var game = NewGame(out var world);
        var owner = game.Join("a", NewSoldier("Owner"), new Recorder());
        var vacant = world.GetYear(owner.Player.CurrentYear).StoreSlots.First(s => s.IsAvailableForPurchase);
        owner.Player.PlaceAt(vacant.Location);
        owner.Player.AddCredits(vacant.PurchaseCost + 10);
        game.Execute(owner, "buy-store");

        var rec = new Recorder();
        var stranger = game.Join("b", NewSoldier("Stranger"), rec);
        stranger.Player.PlaceAt(vacant.Location);

        rec.Clear();
        game.Execute(stranger, "deposit 10");

        Assert.True(rec.Any("You need to be at a store you own"));
    }

    [Fact]
    public void Collect_GathersTheStoreCapitalSeededAtPurchase()
    {
        var game = NewGame(out var world);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);
        var vacant = world.GetYear(session.Player.CurrentYear).StoreSlots.First(s => s.IsAvailableForPurchase);
        session.Player.PlaceAt(vacant.Location);
        session.Player.AddCredits(vacant.PurchaseCost + 10);
        game.Execute(session, "buy-store");
        var startingCapital = vacant.Store!.Capital; // Purchase seeds new stores with starting Capital
        Assert.True(startingCapital > 0);

        rec.Clear();
        game.Execute(session, "collect");

        Assert.True(rec.Any($"Collected {startingCapital} Credits"));
        Assert.Equal(0, vacant.Store.Capital);
    }

    [Fact]
    public void Collect_WithNoOwnedStore_SaysSo()
    {
        var game = NewGame(out _);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);

        rec.Clear();
        game.Execute(session, "collect");

        Assert.True(rec.Any("don't own a store"));
    }

    [Fact]
    public void Leave_DropsTheOnlineCountAndTellsTheOthers()
    {
        var game = NewGame(out _);
        var a = new Recorder();
        game.Join("a", NewSoldier("Ada"), a);
        var sb = game.Join("b", NewSoldier("Bo"), new Recorder());
        a.Clear();

        game.Leave(sb);

        Assert.Equal(1, game.OnlineCount);
        Assert.True(a.Any("Bo") && a.Any("faded"));
    }
}
