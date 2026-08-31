using ChronTravelers.Core.Characters;
using ChronTravelers.Core.Classes;
using ChronTravelers.Core.Items;
using ChronTravelers.Core.Time;
using ChronTravelers.Engine.Simulation;

namespace ChronTravelers.Engine.Tests.Simulation;

public class WorldSimulationTests
{
    private static TimeWorld World() => TestTimeWorld.Build(seed: 909);

    private static Mutant NewMutant(string name, TimeWorld world, int year)
    {
        var mutant = new Mutant(name, CharacterClass.Warrior, startingYear: year);
        mutant.PlaceAt(world.GetYear(year).Map.Start);
        return mutant;
    }

    /// <summary>Parks the player on a coordinate no room occupies, so the spatial monster sim (aggro/ambush) stays out of these bookkeeping-focused tests.</summary>
    private static Mutant OffGridPlayer(string name, TimeWorld world, int year)
    {
        var mutant = NewMutant(name, world, year);
        mutant.PlaceAt(new Core.World.Coordinate(999, 999));
        return mutant;
    }

    [Fact]
    public void Tick_RegeneratesIonsForADepletedMutantInEarlyYears()
    {
        var world = World();
        var player = NewMutant("Player", world, 2000);
        var npc = NewMutant("Vex", world, 2000);
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
        var npc = NewMutant("Vex", world, 2000);
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
        var player = NewMutant("Player", world, 2000);
        var npc = NewMutant("Vex", world, 2000);
        var simulation = new WorldSimulation(world, [npc], StubRandomSource.Fixed(0.5));

        simulation.Tick(player);

        Assert.Contains(simulation.Broadcast.Events, e => e.Message.Contains("was slain by Vex"));
    }

    [Fact]
    public void Tick_PublishesLevelReachedEventWhenAnNpcLevelsUp()
    {
        var world = World();
        var player = NewMutant("Player", world, 2000);
        var npc = NewMutant("Vex", world, 2000);
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
        var player = NewMutant("Player", world, 2200);
        var npc = NewMutant("Vex", world, 2200);
        for (var tier = 1; tier <= 4; tier++)
        {
            npc.AddToInventory(Item.Create($"Junk Tier {tier}", ItemType.Junk, tier, Rarity.Common));
        }

        var simulation = new WorldSimulation(world, [npc], StubRandomSource.Fixed(0.5));

        simulation.Tick(player);

        // Sold at least one junk item to the year's government store.
        Assert.True(npc.Inventory.Count(i => i.Type == ItemType.Junk) < 4);
        Assert.True(npc.Riblets > 0);
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
        var player = NewMutant("Player", world, year);

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
        var player = NewMutant("Player", world, 2000);
        var npcEarly = NewMutant("Vex", world, 2100);
        var npcLate = NewMutant("Corrode", world, 4500);

        var simulation = new WorldSimulation(world, [npcEarly, npcLate], StubRandomSource.Fixed(0.5));

        simulation.Tick(player); // must not throw resolving two NPCs in two different years

        Assert.Contains(simulation.Broadcast.Events, e => e.Message.Contains("Vex"));
        Assert.Contains(simulation.Broadcast.Events, e => e.Message.Contains("Corrode"));
    }

    [Fact]
    public void Tick_RunsTheCurrentYearsMonsterPopulation_InfightKillBroadcasts()
    {
        var world = World();
        var player = NewMutant("Player", world, 2000);
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
}
