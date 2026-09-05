using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Economy;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Stats;
using ChronoTravelers.Core.Time;
using ChronoTravelers.Engine.Npc;
using ChronoTravelers.Engine.Simulation;

namespace ChronoTravelers.Engine.Tests.Simulation;

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

    /// <summary>
    /// An NPC levelled up and given a real weapon, then healed to full —
    /// MonsterScaling calibrates a same-tier monster against a level-10·N
    /// character, so a fresh level-1 NPC can't win (or survive) a grind
    /// fight in its own starting year. Mirrors NpcPopulation.Create's
    /// fast-level-then-top-off.
    /// </summary>
    private static Traveler ArmedNpc(string name, TimeWorld world, int year, int levels)
    {
        var npc = NewTraveler(name, world, year);
        for (var i = 0; i < levels; i++) npc.LevelUp();
        var weapon = Item.Create("Test Blade", ItemType.Weapon, 3, Rarity.Epic);
        npc.AddToInventory(weapon);
        npc.Wield(weapon);
        npc.Health.Heal(npc.Health.Max);
        return npc;
    }

    [Fact]
    public void Tick_RegeneratesTachyonsForADepletedTravelerInEarlyYears()
    {
        var world = World();
        var player = NewTraveler("Player", world, 2000);
        var npc = NewTraveler("Vex", world, 2000);
        // MonsterScaling calibrates a tier-N monster against a level-10·N
        // character, so a fresh level-1 NPC can't survive its own starting
        // year's monsters long enough to show regen — level it into that band.
        for (var i = 0; i < 15; i++) npc.LevelUp();
        player.Tachyons.Spend(player.Tachyons.Current);
        npc.Tachyons.Spend(npc.Tachyons.Current);
        var simulation = new WorldSimulation(world, [npc], StubRandomSource.Fixed(0.5));

        for (var i = 0; i < 15; i++)
        {
            simulation.Tick(player);
        }

        Assert.True(player.Tachyons.Current > 0, "Player Tachyons should regen in early years.");
        Assert.True(npc.Tachyons.Current > 0, "NPC Tachyons should regen in early years.");
    }

    [Fact]
    public void Tick_StillNetDrainsTachyonsDeepInTheFuture()
    {
        var world = World();
        var player = OffGridPlayer("Player", world, 4900);
        var simulation = new WorldSimulation(world, [], StubRandomSource.Fixed(0.5));
        var startingTachyons = player.Tachyons.Current;

        for (var i = 0; i < 20; i++)
        {
            simulation.Tick(player);
        }

        Assert.True(player.Tachyons.Current < startingTachyons, "In the far future the drain should outpace the regen.");
    }

    // (Was Tick_SkipsDeadNpcsEntirely — removed. Its premise, "a dead NPC
    //  stays put and emits nothing, forever", no longer holds now that
    //  WorldSimulation.RespawnDeadNpcs revives a dead slot each tick. The
    //  replacement contract is covered by
    //  Tick_RespawnsADeadLocalPoolNpc_NearThePlayersCurrentYear and
    //  Tick_LeavesALivingNpcAlone_NoRespawn below.)

    [Fact]
    public void Tick_PublishesSlainEventWhenAnNpcDefeatsAMonster()
    {
        var world = World();
        var player = NewTraveler("Player", world, 2000);
        var npc = ArmedNpc("Vex", world, 2000, levels: 14);
        var simulation = new WorldSimulation(world, [npc], StubRandomSource.Fixed(0.5));

        for (var i = 0; i < 6; i++) simulation.Tick(player);

        Assert.Contains(simulation.Broadcast.Events, e => e.Message.Contains("was slain by Vex"));
    }

    [Fact]
    public void Tick_PublishesLevelReachedEventWhenAnNpcLevelsUp()
    {
        var world = World();
        var player = NewTraveler("Player", world, 2000);
        // Vex needs to both *win* a fight (a same-tier monster is calibrated
        // for level 10 now — MonsterScaling) and still be below year 2000's
        // soft level cap of 10 so a kill can level it. Level 8 + a real
        // weapon clears the fight; preload XP to 20 short of level 9 so the
        // first ~40-XP kill tips it over.
        var npc = ArmedNpc("Vex", world, 2000, levels: 7);
        npc.GainXp(Leveling.CumulativeXpForLevel(9) - npc.Xp - 20);
        var simulation = new WorldSimulation(world, [npc], StubRandomSource.Fixed(0.5));

        for (var i = 0; i < 6; i++) simulation.Tick(player);

        Assert.True(npc.Level >= 9, $"Vex should have reached at least level 9 (was {npc.Level}).");
        Assert.Contains(simulation.Broadcast.Events, e => e.Message == "Vex reached level 9!");
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
        monster.RaiseAggro(ChronoTravelers.Core.Monsters.AggroModel.Cap);
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

    [Fact]
    public void Tick_RespawnsADeadLocalPoolNpc_NearThePlayersCurrentYear()
    {
        var world = World();
        var player = OffGridPlayer("Player", world, 3000);
        var deadNpc = NewTraveler("Vex", world, 2000);
        deadNpc.Health.Damage(deadNpc.Health.Max);
        var simulation = new WorldSimulation(world, [deadNpc], StubRandomSource.Fixed(0.5));

        simulation.Tick(player);

        var respawned = simulation.Npcs[0];
        // A genuine replacement: a new instance, fast-levelled into the
        // anchor year's band (so not the leftover corpse), placed within
        // the local spawn spread of the player's current year. (Whether
        // that fresh NPC then survives its first combat tick is a
        // combat-balance question, not a respawn one.)
        Assert.NotSame(deadNpc, respawned);
        Assert.True(respawned.Level > 1, "a respawned local-pool NPC is fast-levelled into its year's band");
        Assert.InRange(respawned.CurrentYear, 3000 - NpcPopulation.LocalSpawnSpreadYears, 3000 + NpcPopulation.LocalSpawnSpreadYears);
    }

    [Fact]
    public void Tick_DrawsMaintenanceFromAnOwnedStoresReserve()
    {
        var world = World();
        var player = OffGridPlayer("Player", world, 2000);
        var npc = NewTraveler("Vex", world, 2000);
        var slot = world.GetYear(2000).StoreSlots.First(s => s.IsAvailableForPurchase);
        var store = slot.RestoreOwnership(npc, capital: 0, creditReserve: 50);

        // 0.99 misses every optional NPC roll (travel/purchase/tend), so this
        // isolates the maintenance pass itself from any NPC store-tending action.
        var simulation = new WorldSimulation(world, [npc], StubRandomSource.Fixed(0.99));

        simulation.Tick(player);

        Assert.True(store.CreditReserve < 50);
        Assert.Equal(0, store.MissedMaintenanceTicks);
    }

    [Fact]
    public void Tick_NeverDrawsMaintenanceFromTheGovernmentStore()
    {
        var world = World();
        var player = OffGridPlayer("Player", world, 2000);
        var government = world.GetYear(2000).StoreSlots.Single(s => s.Store is { IsGovernmentRun: true }).Store!;
        var capitalBefore = government.Capital;

        var simulation = new WorldSimulation(world, [], StubRandomSource.Fixed(0.99));
        simulation.Tick(player);

        Assert.Equal(0, government.CreditReserve);
        Assert.Equal(capitalBefore, government.Capital);
    }

    [Fact]
    public void Tick_RepossessesAnOwnedStoreAfterTooManyMissedMaintenanceTicks_AndBroadcastsIt()
    {
        var world = World();
        var player = OffGridPlayer("Player", world, 2000);
        var npc = NewTraveler("Vex", world, 2000);
        // npc has 0 Credits by default - can't self-fund maintenance either.
        var slot = world.GetYear(2000).StoreSlots.First(s => s.IsAvailableForPurchase);
        slot.RestoreOwnership(npc, capital: 0, creditReserve: 0);

        var simulation = new WorldSimulation(world, [npc], StubRandomSource.Fixed(0.99));

        for (var i = 0; i < Store.ForeclosureThreshold; i++)
        {
            simulation.Tick(player);
        }

        Assert.True(slot.IsAvailableForPurchase);
        Assert.Contains(simulation.Broadcast.Events, e => e.Message.Contains("repossessed") && e.Year == 2000);
    }

    [Fact]
    public void Tick_LeavesALivingNpcAlone_NoRespawn()
    {
        var world = World();
        var player = OffGridPlayer("Player", world, 3000);
        var npc = NewTraveler("Vex", world, 2000);
        var simulation = new WorldSimulation(world, [npc], StubRandomSource.Fixed(0.99));

        simulation.Tick(player);

        Assert.Same(npc, simulation.Npcs[0]);
    }
}
