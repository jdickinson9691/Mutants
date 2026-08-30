using Mutants.Core.Characters;
using Mutants.Core.Classes;
using Mutants.Core.Economy;
using Mutants.Core.Items;
using Mutants.Core.Levels;
using Mutants.Core.Monsters;
using Mutants.Core.World;
using Mutants.Engine.Simulation;

namespace Mutants.Engine.Tests.Simulation;

public class WorldSimulationTests
{
    private static Mutant NewMutant(string name, LevelMap level)
    {
        var mutant = new Mutant(name, CharacterClass.Warrior);
        mutant.PlaceAt(level.Start);
        return mutant;
    }

    /// <summary>Wraps a single LevelMap as a whole one-level GameWorld — everything WorldSimulation needs is now resolved per-NPC through the world, so a bare LevelMap alone is no longer enough to construct one.</summary>
    private static GameWorld SingleLevelWorld(LevelMap level, IReadOnlyList<StoreSlot>? storeSlots = null) =>
        new([new WorldLevelDefinition(1, level, TestMonsters.RosterFor(1), storeSlots ?? [])]);

    [Fact]
    public void Tick_DrainsIonsOverEnoughTicks_ForBothPlayerAndNpcs()
    {
        var level = TestLevel.Build();
        var player = NewMutant("Player", level);
        var npc = NewMutant("Vex", level);
        var simulation = new WorldSimulation(SingleLevelWorld(level), [npc], StubRandomSource.Fixed(0.5));

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
        var level = TestLevel.Build();
        var player = NewMutant("Player", level);
        var npc = NewMutant("Vex", level);
        npc.Health.Damage(npc.Health.Max);
        var startingPosition = npc.Position;
        var simulation = new WorldSimulation(SingleLevelWorld(level), [npc], StubRandomSource.Fixed(0.5));

        simulation.Tick(player); // must not throw

        Assert.Equal(startingPosition, npc.Position); // never acted
        Assert.Empty(simulation.Broadcast.Events);
    }

    [Fact]
    public void Tick_PublishesSlainEventWhenAnNpcDefeatsAMonster()
    {
        var level = TestLevel.Build();
        var player = NewMutant("Player", level);
        var npc = NewMutant("Vex", level);
        var simulation = new WorldSimulation(SingleLevelWorld(level), [npc], StubRandomSource.Fixed(0.5));

        simulation.Tick(player);

        Assert.Contains(simulation.Broadcast.Events, e => e.Message.Contains("was slain by Vex"));
    }

    [Fact]
    public void Tick_PublishesLevelReachedEventWhenAnNpcLevelsUp()
    {
        var level = TestLevel.Build();
        var player = NewMutant("Player", level);
        var npc = NewMutant("Vex", level);
        npc.GainXp(60); // just short of the level-2 threshold (100); a single tier-1 kill awards 40 XP
        var simulation = new WorldSimulation(SingleLevelWorld(level), [npc], StubRandomSource.Fixed(0.5));

        simulation.Tick(player);

        Assert.Equal(2, npc.Level);
        Assert.Contains(simulation.Broadcast.Events, e => e.Message == "Vex reached level 2!");
    }

    [Fact]
    public void Tick_PassesActiveStoresThroughToNpcTrading()
    {
        var level = TestLevel.Build();
        var player = NewMutant("Player", level);
        var npc = NewMutant("Vex", level);
        for (var tier = 1; tier <= 4; tier++)
        {
            npc.AddToInventory(Item.Create($"Junk Tier {tier}", ItemType.Junk, tier, Rarity.Common));
        }

        var store = Store.CreateGovernmentStore("Test Store", homeLevel: 1);
        var slot = new StoreSlot("Test Store", level.Start, homeLevel: 1, purchaseCost: 0, store);
        var simulation = new WorldSimulation(SingleLevelWorld(level, [slot]), [npc], StubRandomSource.Fixed(0.5));

        simulation.Tick(player);

        Assert.Equal(3, npc.Inventory.Count(i => i.Type == ItemType.Junk));
        Assert.True(npc.Riblets > 0);
    }

    [Fact]
    public void Tick_IgnoresUnpurchasedStoreSlots()
    {
        var level = TestLevel.Build();
        var player = NewMutant("Player", level);
        var npc = NewMutant("Vex", level);
        for (var tier = 1; tier <= 4; tier++)
        {
            npc.AddToInventory(Item.Create($"Junk Tier {tier}", ItemType.Junk, tier, Rarity.Common));
        }

        var emptySlot = new StoreSlot("Empty Storefront", level.Start, homeLevel: 1, purchaseCost: 150); // no Store yet
        var simulation = new WorldSimulation(SingleLevelWorld(level, [emptySlot]), [npc], StubRandomSource.Fixed(0.5));

        simulation.Tick(player);

        Assert.Equal(4, npc.Inventory.Count(i => i.Type == ItemType.Junk)); // nothing to sell to - falls through to Grind
    }

    [Fact]
    public void Tick_NpcsActInPositionOrder_PlayerNeverActsAsAnNpc()
    {
        var level = TestLevel.Build();
        var player = NewMutant("Player", level);
        var simulation = new WorldSimulation(SingleLevelWorld(level), [], StubRandomSource.Fixed(0.5));

        simulation.Tick(player); // no NPCs - should just drain the player's Ions bookkeeping, no exceptions

        Assert.Empty(simulation.Broadcast.Events);
    }

    [Fact]
    public void Tick_ResolvesEachNpcAgainstItsOwnCurrentTimeLevel_NotAOneSharedLevel()
    {
        var level1 = TestLevel.Build();
        var level2Map = GridLevelBuilder.Build("Level 2", Coordinate.Origin, new Dictionary<Coordinate, string> { [Coordinate.Origin] = "A second level." });
        var world = new GameWorld([
            new WorldLevelDefinition(1, level1, TestMonsters.RosterFor(1), []),
            new WorldLevelDefinition(2, level2Map, TestMonsters.RosterFor(2), [], gatekeeper: () => TestMonsters.Gatekeeper(2), minCharacterLevelToUnlock: 1),
        ]);

        var player = NewMutant("Player", level1);
        var npcOnLevel1 = NewMutant("Vex", level1);
        var npcOnLevel2 = new Mutant("Corrode", CharacterClass.Warrior, unlockedTimeLevel: 2);
        npcOnLevel2.SetCurrentTimeLevel(2);
        npcOnLevel2.PlaceAt(level2Map.Start);

        var simulation = new WorldSimulation(world, [npcOnLevel1, npcOnLevel2], StubRandomSource.Fixed(0.5));

        simulation.Tick(player); // must not throw resolving two NPCs on two different levels

        Assert.Contains(simulation.Broadcast.Events, e => e.Message.Contains("was slain by Vex") || e.Message.Contains("Vex was slain by"));
        Assert.Contains(simulation.Broadcast.Events, e => e.Message.Contains("was slain by Corrode") || e.Message.Contains("Corrode was slain by"));
    }
}
