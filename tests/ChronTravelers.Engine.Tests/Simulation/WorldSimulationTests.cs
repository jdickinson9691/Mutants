using ChronTravelers.Core.Characters;
using ChronTravelers.Core.Classes;
using ChronTravelers.Core.Items;
using ChronTravelers.Core.Time;
using ChronTravelers.Engine.Simulation;

namespace ChronTravelers.Engine.Tests.Simulation;

public class WorldSimulationTests
{
    private static TimeWorld World() => TestTimeWorld.Build(seed: 909);

    private static Traveler NewTraveler(string name, TimeWorld world, int year)
    {
        var traveler = new Traveler(name, CharacterClass.Soldier, startingYear: year);
        traveler.PlaceAt(world.GetYear(year).Map.Start);
        return traveler;
    }

    /// <summary>Parks the player on a coordinate no room occupies, so the spatial monster sim (aggro/ambush) stays out of these bookkeeping-focused tests.</summary>
    private static Traveler OffGridPlayer(string name, TimeWorld world, int year)
    {
        var traveler = NewTraveler(name, world, year);
        traveler.PlaceAt(new Core.World.Coordinate(999, 999));
        return traveler;
    }

    [Fact]
    public void Tick_RegeneratesIonsForADepletedTravelerInEarlyYears()
    {
        var world = World();
        var player = NewTraveler("Player", world, 2000);
        var npc = NewTraveler("Vex", world, 2000);
        player.Ions.Spend(player.Ions.Current);
        npc.Ions.Spend(npc.Ions.Current);
        var simulation = new WorldSimulation(world, [npc], StubRandomSource.Fixed(0.5));

        for (var i = 0; i < 15; i++)
        {
            simulation.Tick(player);
        }

        Assert.True(player.Ions.Current > 0, "Player Ions should regen in early years.");
        Assert.True(npc.Ions.Current > 0, "NPC Ions should regen in early years.");
    }

    [Fact]
    public void Tick_StillNetDrainsIonsDeepInTheFuture()
    {
        var world = World();
        var player = OffGridPlayer("Player", world, 4900);
        var simulation = new WorldSimulation(world, [], StubRandomSource.Fixed(0.5));
        var startingIons = player.Ions.Current;

        for (var i = 0; i < 20; i++)
        {
            simulation.Tick(player);
        }

        Assert.True(player.Ions.Current < startingIons, "In the far future the drain should outpace the regen.");
    }

    [Fact]
    public void Tick_SkipsDeadNpcsEntirely()
    {
        var world = World();
        var player = OffGridPlayer("Player", world, 2000);
        var npc = NewTraveler("Vex", world, 2000);
        npc.Health.Damage(npc.Health.Max);
        var startingPosition = npc.Position;
        var simulation = new WorldSimulation(world, [npc], StubRandomSource.Fixed(0.5));

        simulation.Tick(player);

        Assert.Equal(startingPosition, npc.Position);
        Assert.Empty(simulation.Broadcast.Events);
    }

    [Fact]
    public void Tick_PublishesSlainEventWhenAnNpcDefeatsAMonster()
    {
        var world = World();
        var player = NewTraveler("Player", world, 2000);
        var npc = NewTraveler("Vex", world, 2000);
        var simulation = new WorldSimulation(world, [npc], StubRandomSource.Fixed(0.5));

        simulation.Tick(player);

        Assert.Contains(simulation.Broadcast.Events, e => e.Message.Contains("was slain by Vex"));
    }

    [Fact]
    public void Tick_PublishesLevelReachedEventWhenAnNpcLevelsUp()
    {
        var world = World();
        var player = NewTraveler("Player", world, 2000);
        var npc = NewTraveler("Vex", world, 2000);
        // A tier-1 kill awards 40 XP; level 2 needs 100 cumulative.
        npc.GainXp(80);
        var simulation = new WorldSimulation(world, [npc], StubRandomSource.Fixed(0.5));

        simulation.Tick(player);

        Assert.Equal(2, npc.Level);
        Assert.Contains(simulation.Broadcast.Events, e => e.Message == "Vex reached level 2!");
    }

    [Fact]
    public void Tick_PassesTheYearsGovernmentStoreThroughToNpcTrading()
    {
        var world = World();
        var player = NewTraveler("Player", world, 2200);
        var npc = NewTraveler("Vex", world, 2200);
        for (var tier = 1; tier <= 4; tier++)
        {
            npc.AddToInventory(Item.Create($"Junk Tier {tier}", ItemType.Junk, tier, Rarity.Common));
        }

        var simulation = new WorldSimulation(world, [npc], StubRandomSource.Fixed(0.5));

        simulation.Tick(player);

        // Sold at least one junk item to the year's government store.
        Assert.True(npc.Inventory.Count(i => i.Type == ItemType.Junk) < 4);
        Assert.True(npc.Credits > 0);
    }

    [Fact]
    public void Tick_WithNoNpcs_JustDoesPlayerBookkeeping()
    {
        var world = World();
        var player = OffGridPlayer("Player", world, 2000);
        var simulation = new WorldSimulation(world, [], StubRandomSource.Fixed(0.5));

        simulation.Tick(player);

        Assert.Empty(simulation.Broadcast.Events);
    }

    [Fact]
    public void Tick_OnlyAmbushesThePlayerOnAnIdleTurn()
    {
        var world = World();
        var year = 2000;
        var player = NewTraveler("Player", world, year);

        // Stand the player on a hostile monster's tile that isn't a store haven.
        var content = world.GetYear(year);
        var storeTiles = content.StoreSlots.Select(s => s.Location).ToHashSet();
        var monster = content.Population.Monsters.First(m => !m.Health.IsDead && !storeTiles.Contains(m.Position));
        monster.RaiseAggro(ChronTravelers.Core.Monsters.AggroModel.Cap);
        player.PlaceAt(monster.Position);

        var simulation = new WorldSimulation(world, [], StubRandomSource.Fixed(0.4));

        var beforeActive = player.Health.Current;
        simulation.Tick(player, playerActedIdly: false); // seed last-position
        simulation.Tick(player, playerActedIdly: false); // acting → safe
        Assert.Equal(beforeActive, player.Health.Current);

        var beforeIdle = player.Health.Current;
        simulation.Tick(player, playerActedIdly: true);  // idle + held position → ambush
        Assert.True(player.Health.Current < beforeIdle);
    }

    [Fact]
    public void Tick_ResolvesEachNpcAgainstItsOwnYear_NotOneSharedYear()
    {
        var world = World();
        var player = NewTraveler("Player", world, 2000);
        var npcEarly = NewTraveler("Vex", world, 2100);
        var npcLate = NewTraveler("Corrode", world, 4500);

        var simulation = new WorldSimulation(world, [npcEarly, npcLate], StubRandomSource.Fixed(0.5));

        simulation.Tick(player); // must not throw resolving two NPCs in two different years

        Assert.Contains(simulation.Broadcast.Events, e => e.Message.Contains("Vex"));
        Assert.Contains(simulation.Broadcast.Events, e => e.Message.Contains("Corrode"));
    }

    [Fact]
    public void Tick_RunsTheCurrentYearsMonsterPopulation_InfightKillBroadcasts()
    {
        var world = World();
        var player = NewTraveler("Player", world, 2000);
        var pop = world.GetYear(player.CurrentYear).Population;

        // Force two of the year's monsters into the same room so an infight is possible.
        var spot = pop.Monsters[0].Position;
        pop.Monsters[1].PlaceAt(spot);
        var before = pop.Monsters.Count;

        // Fixed(0.0): every monster wanders (all picking the same exit index, so the
        // co-located pair stays together), and the infight roll passes.
        var simulation = new WorldSimulation(world, [], StubRandomSource.Fixed(0.0));
        simulation.Tick(player);

        Assert.True(pop.Monsters.Count < before, "An infight should have removed a monster from the current year's population.");
        Assert.Contains(simulation.Broadcast.Events, e => e.Message.Contains("was slain by"));
    }

    [Fact]
    public void Tick_KeepsSimulatingYearsThePlayerHasLeft_InfightingAndBroadcastingThere()
    {
        var world = World();

        // The player visits 2000 (instantiating its population), then leaves for 3000.
        var player = NewTraveler("Player", world, 2000);
        var awayPop = world.GetYear(2000).Population;
        var spot = awayPop.Monsters[0].Position;
        awayPop.Monsters[1].PlaceAt(spot); // set up an infight back in 2000
        var before = awayPop.Monsters.Count;

        player.SetCurrentYear(3000);
        player.PlaceAt(world.GetYear(3000).Map.Start);

        var simulation = new WorldSimulation(world, [], StubRandomSource.Fixed(0.0));
        simulation.Tick(player);

        Assert.True(awayPop.Monsters.Count < before, "A year the player left should still run its monster infights.");
        Assert.Contains(simulation.Broadcast.Events, e => e.Message.Contains("was slain by") && e.Year == 2000);
    }

    [Fact]
    public void Tick_AYearThePlayerHasLeft_NeverAmbushesOrTracksAnyone()
    {
        var world = World();
        var player = NewTraveler("Player", world, 2000);
        var awayPop = world.GetYear(2000).Population;

        // Enrage every monster in 2000, then leave. Unattended, none of this
        // should touch the player (who is now in 3000).
        foreach (var m in awayPop.Monsters)
        {
            m.RaiseAggro(Core.Monsters.AggroModel.Cap);
        }

        player.SetCurrentYear(3000);
        world.GetYear(3000); // instantiate it
        player.PlaceAt(new Core.World.Coordinate(999, 999)); // off-grid in 3000 too, so only the "away year" is under test
        var fullHp = player.Health.Current;

        var simulation = new WorldSimulation(world, [], StubRandomSource.Fixed(0.5));
        for (var i = 0; i < 10; i++)
        {
            simulation.Tick(player, playerActedIdly: true);
        }

        Assert.Equal(fullHp, player.Health.Current);
        Assert.DoesNotContain(simulation.Broadcast.Events, e => e.Message.Contains("ambushes"));
    }
}
