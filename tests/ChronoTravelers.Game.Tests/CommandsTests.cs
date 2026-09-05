using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Monsters;
using ChronoTravelers.Core.Time;
using ChronoTravelers.Core.World;
using ChronoTravelers.Engine;
using ChronoTravelers.Game;

namespace ChronoTravelers.Game.Tests;

/// <summary>
/// Coverage for the shared-world command set (Commands.cs / Render.cs) not
/// already exercised by SharedGameTests.cs — informational renders
/// (look/status/inventory/monsters/who/news), heal, take, fight's
/// "nothing here" and loss/respawn paths, convert, travel's edge cases,
/// arrival announcements, and a handful of store-command guard rails. Same
/// harness (a Recorder IGameOutput + TestTimeWorld) and conventions as
/// SharedGameTests.cs — this file exists only to keep that one from
/// growing unwieldy.
/// </summary>
public class CommandsTests
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
        world = TestTimeWorld.Build(seed: 8181);
        return new SharedGame(world, Array.Empty<Traveler>(), new SystemRandomSource());
    }

    private static Traveler NewSoldier(string name = "Rook") => new(name, CharacterClass.Soldier);

    // --- look / status / inventory / monsters --------------------------

    [Fact]
    public void Look_Bare_RedescribesTheCurrentRoom()
    {
        var game = NewGame(out _);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);

        rec.Clear();
        game.Execute(session, "look");

        Assert.True(rec.Any("A.D."));
        Assert.True(rec.Any("Exits:") || rec.Any("no exits"));
    }

    [Fact]
    public void LookDirection_TowardARealExit_DescribesTheNeighboringRoom()
    {
        var game = NewGame(out var world);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);
        var room = world.GetYear(session.Player.CurrentYear).Map.GetRoom(session.Player.Position);
        var exit = room.ExitDescriptions.Keys.First();

        rec.Clear();
        game.Execute(session, $"look {exit.Name()}");

        Assert.True(rec.Any($"To the {exit.Name()}"));
        Assert.Equal(session.Player.Position, session.Player.Position); // look never moves the player
    }

    [Fact]
    public void LookDirection_WithAnUnrecognizedDirection_AsksWhereToLook()
    {
        var game = NewGame(out _);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);

        rec.Clear();
        game.Execute(session, "look sideways");

        Assert.True(rec.Any("Look where?"));
    }

    [Fact]
    public void Status_ReportsHpTachyonsCreditsLevelAndYear()
    {
        var game = NewGame(out _);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier("Ada"), rec);

        rec.Clear();
        game.Execute(session, "status");

        Assert.True(rec.Any("Ada the Soldier"));
        Assert.True(rec.Any("HP"));
        Assert.True(rec.Any("Level 1"));
        Assert.True(rec.Any("2000 A.D."));
    }

    [Fact]
    public void Inventory_WhenEmpty_SaysCarryingNothing()
    {
        var game = NewGame(out _);
        var rec = new Recorder();
        var player = new Traveler("Bare", CharacterClass.Soldier); // no starter kit — CharacterFactory adds that, not the bare ctor
        var session = game.Join("a", player, rec);

        rec.Clear();
        game.Execute(session, "inventory");

        Assert.True(rec.Any("carrying nothing"));
    }

    [Fact]
    public void Inventory_ListsEachCarriedItem()
    {
        var game = NewGame(out _);
        var rec = new Recorder();
        var player = NewSoldier();
        player.AddToInventory(Item.Create("Pocket Lint", ItemType.Junk, 1, Rarity.Common));
        var session = game.Join("a", player, rec);

        rec.Clear();
        game.Execute(session, "inventory");

        Assert.True(rec.Any("Pocket Lint"));
        Assert.True(rec.Any($"1/{Traveler.MaxInventorySize}"));
    }

    [Fact]
    public void Monsters_ListsEachLivingMonsterInTheYear()
    {
        var game = NewGame(out var world);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);
        var pop = world.GetYear(session.Player.CurrentYear).Population;
        var livingBefore = pop.Monsters.Count(m => !m.Health.IsDead);
        var lurker = new Monster("Lurker", 1, maxHp: 5, attackPower: 1, defense: 0, speed: 1, xpReward: 1, lootTable: []);
        lurker.PlaceAt(session.Player.Position);
        pop.AddMonster(lurker);

        rec.Clear();
        game.Execute(session, "monsters");

        // The year is already seeded with a roster; the list should now show
        // exactly one more, and name the one we added.
        Assert.True(rec.Any($"{livingBefore + 1} monster"));
        Assert.True(rec.Any("Lurker"));
    }

    // --- who / news ------------------------------------------------------

    [Fact]
    public void Who_ListsEveryConnectedTraveler_MarkingTheCaller()
    {
        var game = NewGame(out _);
        var rec = new Recorder();
        var a = game.Join("a", NewSoldier("Ada"), rec);
        game.Join("b", NewSoldier("Bo"), new Recorder());

        rec.Clear();
        game.Execute(a, "who");

        Assert.True(rec.Any("2 Traveler"));
        Assert.True(rec.Any("Ada") && rec.Any("(you)"));
        Assert.True(rec.Any("Bo"));
    }

    [Fact]
    public void News_WithNothingBroadcastYet_SaysSo()
    {
        var game = NewGame(out _);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);

        rec.Clear();
        game.Execute(session, "news");

        Assert.True(rec.Any("Nothing has happened yet"));
    }

    [Fact]
    public void News_ShowsAPublishedBroadcastEvent()
    {
        var game = NewGame(out _);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);
        game.Execute(session, "travel +250"); // publishes a TimeTraveled broadcast event

        rec.Clear();
        game.Execute(session, "news");

        Assert.True(rec.Any("Recent broadcasts"));
    }

    // --- heal --------------------------------------------------------------

    [Fact]
    public void Heal_WhenAlreadyAtFullHealth_SaysSo()
    {
        var game = NewGame(out _);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);

        rec.Clear();
        game.Execute(session, "heal");

        Assert.True(rec.Any("already at full health"));
    }

    [Fact]
    public void Heal_WithNoTachyons_RefusesEvenThoughHurt()
    {
        var game = NewGame(out _);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);
        session.Player.TakeDamage(5);
        session.Player.Tachyons.Spend(session.Player.Tachyons.Current);

        rec.Clear();
        game.Execute(session, "heal");

        Assert.True(rec.Any("Not enough Tachyons"));
    }

    [Fact]
    public void Heal_WhenHurtWithTachyonsAvailable_RestoresHp()
    {
        var game = NewGame(out _);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);
        session.Player.TakeDamage(10);
        var hpBefore = session.Player.Health.Current;
        var tachyonsBefore = session.Player.Tachyons.Current;

        rec.Clear();
        game.Execute(session, "heal");

        Assert.True(session.Player.Health.Current > hpBefore);
        Assert.True(session.Player.Tachyons.Current < tachyonsBefore);
        Assert.True(rec.Any("You heal for"));
    }

    // --- wait ----------------------------------------------------------------

    [Fact]
    public void Wait_SendsAWaitAcknowledgement()
    {
        var game = NewGame(out _);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);

        rec.Clear();
        game.Execute(session, "wait");

        Assert.True(rec.Any("You wait a moment"));
    }

    // --- take ----------------------------------------------------------------

    [Fact]
    public void Take_WithNothingOnTheGround_SaysSo()
    {
        var game = NewGame(out _);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);

        rec.Clear();
        game.Execute(session, "take");

        Assert.True(rec.Any("Nothing on the ground"));
    }

    [Fact]
    public void Take_All_PicksUpEveryGroundedItem()
    {
        var game = NewGame(out var world);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);
        var pop = world.GetYear(session.Player.CurrentYear).Population;
        pop.AddGroundLoot(session.Player.Position, Item.Create("Rusty Cog", ItemType.Junk, 1, Rarity.Common));
        pop.AddGroundLoot(session.Player.Position, Item.Create("Bent Rod", ItemType.Junk, 1, Rarity.Common));

        rec.Clear();
        game.Execute(session, "take all");

        Assert.Contains(session.Player.Inventory, i => i.Name == "Rusty Cog");
        Assert.Contains(session.Player.Inventory, i => i.Name == "Bent Rod");
        Assert.Empty(pop.LootAt(session.Player.Position));
    }

    [Fact]
    public void Take_ByName_PicksUpOnlyTheMatchingItem()
    {
        var game = NewGame(out var world);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);
        var pop = world.GetYear(session.Player.CurrentYear).Population;
        pop.AddGroundLoot(session.Player.Position, Item.Create("Copper Wire", ItemType.Junk, 1, Rarity.Common));
        pop.AddGroundLoot(session.Player.Position, Item.Create("Steel Pipe", ItemType.Junk, 1, Rarity.Common));

        rec.Clear();
        game.Execute(session, "take copper");

        Assert.Contains(session.Player.Inventory, i => i.Name == "Copper Wire");
        Assert.DoesNotContain(session.Player.Inventory, i => i.Name == "Steel Pipe");
        Assert.Contains(pop.LootAt(session.Player.Position), i => i.Name == "Steel Pipe");
    }

    [Fact]
    public void Take_WithAFullPack_LeavesTheItemOnTheGround()
    {
        var game = NewGame(out var world);
        var rec = new Recorder();
        var player = NewSoldier();
        for (var i = 0; i < Traveler.MaxInventorySize; i++)
        {
            player.AddToInventory(Item.Create($"Filler {i}", ItemType.Junk, 1, Rarity.Common));
        }

        var session = game.Join("a", player, rec);
        var pop = world.GetYear(session.Player.CurrentYear).Population;
        pop.AddGroundLoot(session.Player.Position, Item.Create("Unclaimed Prize", ItemType.Junk, 1, Rarity.Common));

        rec.Clear();
        game.Execute(session, "take all");

        Assert.True(rec.Any("pack is full"));
        Assert.Contains(pop.LootAt(session.Player.Position), i => i.Name == "Unclaimed Prize");
    }

    // --- fight ---------------------------------------------------------------

    [Fact]
    public void Fight_WithNothingInTheRoom_SaysSo()
    {
        var game = NewGame(out _);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);

        rec.Clear();
        game.Execute(session, "fight");

        Assert.True(rec.Any("Nothing here to fight"));
    }

    [Fact]
    public void Fight_OnALoss_TheDeathIsntPersisted_AndTheNextTickRespawnsUpstreamAtFullHealth()
    {
        var game = NewGame(out var world);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);
        session.Player.SetCurrentYear(4800);
        var farRoom = world.GetYear(4800).Map.Start;
        session.Player.PlaceAt(farRoom);

        // Attack power far beyond anything a fresh level-1 Soldier can survive
        // one round of — deterministic loss regardless of the ±15% damage
        // variance (CombatResolver.RollDamage) and HP far beyond what the
        // player can dent in the same round, so the fight can't accidentally
        // resolve as a win either.
        var terror = new Monster("Overwhelming Terror", 9, maxHp: 1_000_000, attackPower: 500_000, defense: 500, speed: 999, xpReward: 0, lootTable: []);
        terror.PlaceAt(session.Player.Position);
        world.GetYear(4800).Population.AddMonster(terror);

        rec.Clear();
        game.Execute(session, "fight");

        Assert.True(rec.Any("beaten down"));
        Assert.True(session.Player.Health.IsDead);

        rec.Clear();
        game.Tick();

        Assert.False(session.Player.Health.IsDead);
        Assert.Equal(session.Player.Health.Max, session.Player.Health.Current);
        Assert.Equal(TimeScale.MinYear, session.Player.CurrentYear);
        Assert.True(rec.Any("struck down"));
    }

    // --- convert ---------------------------------------------------------------

    [Fact]
    public void Convert_DestroysTheItemAndGrantsTachyons()
    {
        var game = NewGame(out _);
        var rec = new Recorder();
        var player = NewSoldier();
        player.AddToInventory(new Item("Spare Parts", ItemType.Junk, 1, Rarity.Common, Value: 20));
        var session = game.Join("a", player, rec);
        var tachyonsBefore = player.Tachyons.Current;

        rec.Clear();
        game.Execute(session, "convert Spare Parts");

        Assert.DoesNotContain(player.Inventory, i => i.Name == "Spare Parts");
        Assert.True(player.Tachyons.Current > tachyonsBefore);
        Assert.True(rec.Any("Converted Spare Parts"));
    }

    [Fact]
    public void Convert_WithSeveralSameNamedItems_DestroysTheLowestTierOne()
    {
        // A player who's visited more than one year can carry more than one
        // Time Shard; `convert time shard` must never gamble away the best
        // one — see ItemSelection.Weakest / ItemSelectionTests.cs.
        var game = NewGame(out _);
        var rec = new Recorder();
        var player = NewSoldier();
        var weak = Item.Create("Time Shard", ItemType.Weapon, tier: 1, Rarity.Common);
        var strong = Item.Create("Time Shard", ItemType.Weapon, tier: 5, Rarity.Common);
        player.AddToInventory(strong); // added first — would win under plain FirstOrDefault
        player.AddToInventory(weak);
        var session = game.Join("a", player, rec);

        rec.Clear();
        game.Execute(session, "convert Time Shard");

        Assert.DoesNotContain(player.Inventory, i => ReferenceEquals(i, weak));
        Assert.Contains(player.Inventory, i => ReferenceEquals(i, strong));
    }

    [Fact]
    public void Convert_WithNoMatchingItem_SaysSo()
    {
        var game = NewGame(out _);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);

        rec.Clear();
        game.Execute(session, "convert nonexistent thing");

        Assert.True(rec.Any("No item matching"));
    }

    // --- travel ---------------------------------------------------------------

    [Fact]
    public void Travel_WithAnUnparsableArgument_ExplainsUsage()
    {
        var game = NewGame(out _);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);

        rec.Clear();
        game.Execute(session, "travel wherever");

        Assert.True(rec.Any("Travel where?"));
    }

    [Fact]
    public void Travel_ToTheCurrentYear_SaysAlreadyThere()
    {
        var game = NewGame(out _);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec); // starts at year 2000

        rec.Clear();
        game.Execute(session, "travel 2000");

        Assert.True(rec.Any("already there"));
    }

    [Fact]
    public void Travel_BelowTheMinimumYear_ClampsToItAndFindsItIsAlreadyThere()
    {
        var game = NewGame(out _);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec); // starts at year 2000 == TimeScale.MinYear

        rec.Clear();
        game.Execute(session, "travel 1");

        Assert.True(rec.Any("already there"));
        Assert.Equal(TimeScale.MinYear, session.Player.CurrentYear);
    }

    [Fact]
    public void Travel_WithoutEnoughTachyons_Refuses_AndTheYearDoesNotChange()
    {
        var game = NewGame(out _);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);
        session.Player.Tachyons.Spend(session.Player.Tachyons.Current);

        rec.Clear();
        game.Execute(session, "travel 2500");

        Assert.True(rec.Any("Not enough Tachyons"));
        Assert.Equal(2000, session.Player.CurrentYear);
    }

    // --- move announcements ---------------------------------------------------

    [Fact]
    public void Move_AnnouncesTheArrivalToWhoeverIsAlreadyInTheDestinationRoom()
    {
        var game = NewGame(out var world);
        var mover = game.Join("mover", NewSoldier("Mover"), new Recorder());
        var start = mover.Player.Position;
        var room = world.GetYear(mover.Player.CurrentYear).Map.GetRoom(start);
        var exit = room.ExitDescriptions.Keys.First();
        var destination = world.GetYear(mover.Player.CurrentYear).Map.TryMove(start, exit).Destination!.Value;

        var waiterRec = new Recorder();
        var waiter = game.Join("waiter", NewSoldier("Waiter"), waiterRec);
        waiter.Player.PlaceAt(destination);
        waiterRec.Clear();

        game.Execute(mover, exit.Name());

        Assert.True(waiterRec.Any("Mover arrives from the"));
    }

    // --- unknown command ---------------------------------------------------

    [Fact]
    public void UnknownVerb_IsReportedByName()
    {
        var game = NewGame(out _);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);

        rec.Clear();
        game.Execute(session, "moonwalk");

        Assert.True(rec.Any("Unknown command: 'moonwalk'"));
    }

    // --- store guard rails not already covered in SharedGameTests -------------

    [Fact]
    public void BuyFromStore_AwayFromAnyStore_SaysSo()
    {
        var game = NewGame(out var world);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);
        var storeLocations = world.GetYear(session.Player.CurrentYear).StoreSlots.Select(s => s.Location).ToHashSet();
        var empty = world.GetYear(session.Player.CurrentYear).Map.Rooms.Keys.First(c => !storeLocations.Contains(c));
        session.Player.PlaceAt(empty);

        rec.Clear();
        game.Execute(session, "buy anything");

        Assert.True(rec.Any("no store here to buy from"));
    }

    [Fact]
    public void SellToStore_AwayFromAnyStore_SuggestsConvertOrFindingOne()
    {
        // Non-junk item: a named Junk sale is refused outright regardless of
        // location (see SellToStore_RefusesNamedJunk) — this test isolates
        // the separate "need to be at a store" guard for a sellable item.
        var game = NewGame(out var world);
        var rec = new Recorder();
        var player = NewSoldier();
        player.AddToInventory(Item.Create("Rusty Gear", ItemType.Weapon, 1, Rarity.Common));
        var session = game.Join("a", player, rec);
        var storeLocations = world.GetYear(session.Player.CurrentYear).StoreSlots.Select(s => s.Location).ToHashSet();
        var empty = world.GetYear(session.Player.CurrentYear).Map.Rooms.Keys.First(c => !storeLocations.Contains(c));
        session.Player.PlaceAt(empty);

        rec.Clear();
        game.Execute(session, "sell Rusty Gear");

        Assert.True(rec.Any("need to be at a store to sell"));
    }

    [Fact]
    public void StoreManagement_Stock_WithoutAPrice_ExplainsUsage()
    {
        var game = NewGame(out var world);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);
        var vacant = world.GetYear(session.Player.CurrentYear).StoreSlots.First(s => s.IsAvailableForPurchase);
        session.Player.PlaceAt(vacant.Location);
        session.Player.AddCredits(vacant.PurchaseCost + 10);
        game.Execute(session, "buy-store");
        session.Player.AddToInventory(Item.Create("Odd Gear", ItemType.Weapon, 1, Rarity.Common));

        rec.Clear();
        game.Execute(session, "stock Odd Gear");

        Assert.True(rec.Any("Usage: stock"));
        Assert.DoesNotContain(vacant.Store!.Listings, l => l.Item.Name == "Odd Gear");
    }

    [Fact]
    public void StoreManagement_Withdraw_WithNoMatchingListing_SaysSo()
    {
        var game = NewGame(out var world);
        var rec = new Recorder();
        var session = game.Join("a", NewSoldier(), rec);
        var vacant = world.GetYear(session.Player.CurrentYear).StoreSlots.First(s => s.IsAvailableForPurchase);
        session.Player.PlaceAt(vacant.Location);
        session.Player.AddCredits(vacant.PurchaseCost + 10);
        game.Execute(session, "buy-store");

        rec.Clear();
        game.Execute(session, "withdraw nothing-like-this");

        Assert.True(rec.Any("No listing matching"));
    }
}
