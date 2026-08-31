using Mutants.Core.Characters;
using Mutants.Core.Classes;
using Mutants.Core.Items;
using Mutants.Core.Time;
using Mutants.Engine.Simulation;

namespace Mutants.Engine.Tests.Simulation;

public class WorldSimulationTests
{
    private static TimeWorld World() => TestTimeWorld.Build(seed: 909);

    private static Mutant NewMutant(string name, TimeWorld world, int year)
    {
        var mutant = new Mutant(name, CharacterClass.Warrior, startingYear: year);
        mutant.PlaceAt(world.GetYear(year).Map.Start);
        return mutant;
    }

    [Fact]
    public void Tick_DrainsIonsOverEnoughTicks_ForBothPlayerAndNpcs()
    {
        var world = World();
        var player = NewMutant("Player", world, 2000);
        var npc = NewMutant("Vex", world, 2000);
        var simulation = new WorldSimulation(world, [npc], StubRandomSource.Fixed(0.5));

        var startingPlayerIons = player.Ions.Current;
        var startingNpcIons = npc.Ions.Current;

        for (var i = 0; i < 20; i++)
        {
            simulation.Tick(player);
        }

        Assert.True(player.Ions.Current < startingPlayerIons, "Player Ions should drain over enough ticks.");
        Assert.True(npc.Ions.Current < startingNpcIons, "NPC Ions should drain over enough ticks.");
    }

    [Fact]
    public void Tick_SkipsDeadNpcsEntirely()
    {
        var world = World();
        var player = NewMutant("Player", world, 2000);
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
        var player = NewMutant("Player", world, 2000);
        var simulation = new WorldSimulation(world, [], StubRandomSource.Fixed(0.5));

        simulation.Tick(player);

        Assert.Empty(simulation.Broadcast.Events);
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
}
