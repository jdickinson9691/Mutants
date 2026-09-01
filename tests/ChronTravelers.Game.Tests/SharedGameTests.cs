using ChronTravelers.Core.Characters;
using ChronTravelers.Core.Classes;
using ChronTravelers.Core.Items;
using ChronTravelers.Core.Monsters;
using ChronTravelers.Core.Time;
using ChronTravelers.Engine;
using ChronTravelers.Game;

namespace ChronTravelers.Game.Tests;

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
    public void Tick_RunsIonBookkeepingForEveryConnectedPlayer()
    {
        // The deep future net-drains Ions (GDD §2.1) — a good check that the
        // per-tick loop touches every connected player, not just one.
        var game = NewGame(out _);
        var p1 = NewSoldier("A");
        var p2 = NewSoldier("B");
        game.Join("a", p1, new Recorder());
        game.Join("b", p2, new Recorder());
        p1.SetCurrentYear(5000);
        p2.SetCurrentYear(5000);

        var before1 = p1.Ions.Current;
        var before2 = p2.Ions.Current;

        for (var i = 0; i < 120; i++)
        {
            game.Tick();
        }

        Assert.True(p1.Ions.Current < before1, "player 1 Ions should have net-drained in 5000 A.D.");
        Assert.True(p2.Ions.Current < before2, "player 2 Ions should have net-drained in 5000 A.D.");
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
    public void Travel_DeductsIonsAndMovesTheYear()
    {
        var game = NewGame(out _);
        var session = game.Join("a", NewSoldier(), new Recorder());
        var ionsBefore = session.Player.Ions.Current;

        game.Execute(session, "travel +250");

        Assert.Equal(2250, session.Player.CurrentYear);
        Assert.True(session.Player.Ions.Current < ionsBefore);
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
