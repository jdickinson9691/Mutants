using ChronTravelers.Core.Characters;
using ChronTravelers.Core.Classes;
using ChronTravelers.Core.Events;
using ChronTravelers.Core.Items;
using ChronTravelers.Core.Monsters;
using ChronTravelers.Core.Time;
using ChronTravelers.Core.World;
using ChronTravelers.Engine.Npc;

namespace ChronTravelers.Engine.Tests.Npc;

public class MonsterControllerTests
{
    private static LevelMap FourRoomMap() => GridLevelBuilder.Build(
        "Test", Coordinate.Origin,
        new Dictionary<Coordinate, string>
        {
            [new Coordinate(0, 0)] = "a.",
            [new Coordinate(1, 0)] = "b.",
            [new Coordinate(0, 1)] = "c.",
            [new Coordinate(1, 1)] = "d.",
        });

    private static LevelMap CorridorMap(int length) => GridLevelBuilder.Build(
        "Hall", Coordinate.Origin,
        Enumerable.Range(0, length).ToDictionary(x => new Coordinate(x, 0), x => $"room {x}."));

    /// <summary>An empty-roster population (no auto-placed monsters) so tests can hand-place exactly what they need.</summary>
    private static YearPopulation EmptyPopulation(LevelMap map) =>
        YearPopulation.Seed(worldSeed: 1, year: 2000, map, roster: [], wardenFactory: null);

    /// <summary>A player parked well off the little test grids, so aggro/ambush never engage unless a test opts in.</summary>
    private static Traveler OffMapPlayer()
    {
        var player = new Traveler("Bystander", CharacterClass.Soldier);
        player.PlaceAt(new Coordinate(9, 9));
        return player;
    }

    /// <summary>Thin wrapper over MonsterController.Tick with test-friendly defaults (prev position = "didn't move", not lingering, no havens).</summary>
    private static void Tick(
        YearPopulation pop, LevelMap map, Traveler player, IRandomSource random,
        Coordinate? previousPlayerPosition = null,
        bool playerLingered = false,
        BroadcastChannel? broadcast = null,
        IReadOnlySet<Coordinate>? safeRooms = null,
        IReadOnlyList<Func<Monster>>? roster = null,
        ICollection<string>? narration = null)
        => MonsterController.Tick(pop, map, roster ?? [], player.CurrentYear, player,
            previousPlayerPosition ?? player.Position, playerLingered, random,
            broadcast ?? new BroadcastChannel(), safeRooms, narration);

    private static Monster Lurker(Coordinate at)
    {
        var m = new Monster("Lurker", 1, maxHp: 30, attackPower: 12, defense: 2, speed: 8, xpReward: 40);
        m.PlaceAt(at);
        return m;
    }

    // --- baseline behaviour -------------------------------------------------

    [Fact]
    public void Tick_WandersALivingMonsterToAValidAdjacentRoom()
    {
        var map = FourRoomMap();
        var pop = EmptyPopulation(map);
        var monster = Monster.Create("Rover", tier: 1);
        monster.PlaceAt(Coordinate.Origin);
        pop.AddMonster(monster);

        Tick(pop, map, OffMapPlayer(), StubRandomSource.Fixed(0.0));

        Assert.NotEqual(Coordinate.Origin, monster.Position);
        Assert.True(map.Rooms.ContainsKey(monster.Position));
    }

    [Fact]
    public void Tick_AHurtMonsterWithIonsHeals()
    {
        var map = FourRoomMap();
        var pop = EmptyPopulation(map);
        var monster = new Monster("Bleeder", 2, maxHp: 40, attackPower: 8, defense: 3, speed: 10, xpReward: 80, maxIons: 30);
        monster.Health.Damage(30);
        monster.PlaceAt(Coordinate.Origin);
        pop.AddMonster(monster);

        Tick(pop, map, OffMapPlayer(), StubRandomSource.Fixed(0.99));

        Assert.True(monster.Health.Current > 10, "A hurt monster with Ions should heal.");
        Assert.True(monster.Ions.Current < 30, "Healing should spend Ions.");
    }

    [Fact]
    public void Tick_AHurtBrokeMonsterConvertsACarriedItemThenHeals()
    {
        var map = FourRoomMap();
        var pop = EmptyPopulation(map);
        var monster = new Monster("Scrounger", 2, maxHp: 40, attackPower: 8, defense: 3, speed: 10, xpReward: 80, maxIons: 30);
        monster.Health.Damage(32);
        monster.Ions.Spend(monster.Ions.Current);
        monster.AddToInventory(Item.Create("Circuit Scrap", ItemType.Junk, 3, Rarity.Uncommon));
        monster.PlaceAt(Coordinate.Origin);
        pop.AddMonster(monster);

        Tick(pop, map, OffMapPlayer(), StubRandomSource.Fixed(0.99));

        Assert.Empty(monster.Inventory);
        Assert.True(monster.Health.Current > 8, "It should convert the scrap for Ions and heal.");
    }

    [Fact]
    public void Tick_ACalmHealthyMonsterOnAPileOfLootLeavesItAlone()
    {
        var map = FourRoomMap();
        var pop = EmptyPopulation(map);
        var monster = Monster.Create("Rover", tier: 1); // full HP, full Ions
        monster.PlaceAt(Coordinate.Origin);
        pop.AddMonster(monster);
        pop.AddGroundLoot(Coordinate.Origin, Item.Create("Junk", ItemType.Junk, 1, Rarity.Common));
        pop.AddGroundLoot(Coordinate.Origin, Item.Create("Tonic", ItemType.Consumable, 1, Rarity.Common));

        Tick(pop, map, OffMapPlayer(), StubRandomSource.Fixed(0.99)); // 0.99 -> doesn't wander

        Assert.Empty(monster.Inventory);
        Assert.Equal(2, pop.LootAt(Coordinate.Origin).Count);
    }

    [Fact]
    public void Tick_ALowIonMonsterScavengesOneNonWeaponFromThePileToBurn()
    {
        var map = FourRoomMap();
        var pop = EmptyPopulation(map);
        var monster = new Monster("Scrounger", 2, 40, 8, 3, 10, 80, maxIons: 30);
        monster.Ions.Spend(monster.Ions.Current); // bone dry -> below the scavenge threshold
        monster.PlaceAt(Coordinate.Origin);
        pop.AddMonster(monster);
        pop.AddGroundLoot(Coordinate.Origin, Item.Create("Prime Blade", ItemType.Weapon, 5, Rarity.Epic));
        pop.AddGroundLoot(Coordinate.Origin, Item.Create("Scrap", ItemType.Junk, 1, Rarity.Common));

        Tick(pop, map, OffMapPlayer(), StubRandomSource.Fixed(0.99));

        // Took exactly one item, and left the good weapon for the player.
        Assert.Single(monster.Inventory.Where(i => i.Name == "Scrap"));
        Assert.Contains(pop.LootAt(Coordinate.Origin), i => i.Name == "Prime Blade");
        Assert.DoesNotContain(pop.LootAt(Coordinate.Origin), i => i.Name == "Scrap");
    }

    [Fact]
    public void Tick_AMonsterUpgradesToABetterGroundWeaponAndHitsHarderForIt()
    {
        var map = FourRoomMap();
        var pop = EmptyPopulation(map);
        var monster = Monster.Create("Brute", tier: 1); // full Ions -> only the weapon rule applies
        var baseAttack = monster.EffectiveAttackPower;
        monster.PlaceAt(Coordinate.Origin);
        pop.AddMonster(monster);
        var goodWeapon = Item.Create("Prime Blade", ItemType.Weapon, 5, Rarity.Epic); // large AttackBonus
        pop.AddGroundLoot(Coordinate.Origin, goodWeapon);

        Tick(pop, map, OffMapPlayer(), StubRandomSource.Fixed(0.99));

        Assert.Same(goodWeapon, monster.EquippedWeapon);
        Assert.True(monster.EffectiveAttackPower > baseAttack);
        Assert.Empty(pop.LootAt(Coordinate.Origin));
    }

    [Fact]
    public void Tick_ARoamingMonsterPausesBrieflyAfterMoving()
    {
        var map = FourRoomMap();
        var pop = EmptyPopulation(map);
        var monster = Monster.Create("Roamer", tier: 1);
        monster.PlaceAt(Coordinate.Origin);
        pop.AddMonster(monster);
        var player = OffMapPlayer();

        // Fixed(0.0): it steps this tick, then rolls into a short pause.
        Tick(pop, map, player, StubRandomSource.Fixed(0.0));
        var pausedAt = monster.Position;
        Assert.NotEqual(Coordinate.Origin, pausedAt);
        var rest = monster.RestTicks;
        Assert.True(rest > 0, "it should pause after moving");

        // It holds position for the length of the pause.
        for (var i = 0; i < rest; i++)
        {
            Tick(pop, map, player, StubRandomSource.Fixed(0.0));
            Assert.Equal(pausedAt, monster.Position);
        }
    }

    [Fact]
    public void Tick_NarratesAMonsterEnteringThePlayersRoomWithTheDirectionItCameFrom()
    {
        // Two-room corridor so the monster's only open exit is toward the player.
        var map = CorridorMap(2);
        var pop = EmptyPopulation(map);
        var player = new Traveler("Prey", CharacterClass.Soldier);
        player.PlaceAt(Coordinate.Origin);

        var m = Monster.Create("Beast", tier: 1);
        m.PlaceAt(new Coordinate(1, 0)); // one room east
        pop.AddMonster(m);

        var log = new List<string>();
        Tick(pop, map, player, StubRandomSource.Fixed(0.0), narration: log);

        Assert.Equal(Coordinate.Origin, m.Position);
        Assert.Contains(log, l => l.Contains("Beast") && l.Contains("east"));
    }

    [Fact]
    public void Tick_NarratesAMonsterLeavingThePlayersRoomWithTheDirectionItWent()
    {
        // Two-room corridor: from the player's room the only open exit is east.
        var map = CorridorMap(2);
        var pop = EmptyPopulation(map);
        var player = new Traveler("Prey", CharacterClass.Soldier);
        player.PlaceAt(Coordinate.Origin);

        var m = Monster.Create("Beast", tier: 1);
        m.PlaceAt(Coordinate.Origin); // sharing the player's room
        pop.AddMonster(m);

        var log = new List<string>();
        Tick(pop, map, player, StubRandomSource.Fixed(0.0), narration: log);

        Assert.Equal(new Coordinate(1, 0), m.Position);
        Assert.Contains(log, l => l.Contains("Beast") && l.Contains("east"));
    }

    [Fact]
    public void Tick_NarratesAMonsterFirstComingWithinOneRoom()
    {
        // Three-room corridor: from the far room the only open exit is west,
        // toward the player.
        var map = CorridorMap(3);
        var pop = EmptyPopulation(map);
        var player = new Traveler("Prey", CharacterClass.Soldier);
        player.PlaceAt(Coordinate.Origin);

        var m = Monster.Create("Beast", tier: 1);
        m.PlaceAt(new Coordinate(2, 0)); // two rooms east
        pop.AddMonster(m);

        var log = new List<string>();
        Tick(pop, map, player, StubRandomSource.Fixed(0.0), narration: log);

        Assert.Equal(new Coordinate(1, 0), m.Position);
        Assert.Contains(log, l => l.Contains("hear") || l.Contains("stir") || l.Contains("movement") || l.Contains("shuffle") || l.Contains("shift"));
    }

    [Fact]
    public void Tick_ARoamingMonsterMovesSlowly_HoldingStillWhenTheWanderRollFails()
    {
        // A roll at/above WanderChance means "don't step this tick" — the
        // monster stays put so a player heading for it can actually arrive.
        var map = CorridorMap(8);
        var pop = EmptyPopulation(map);
        var monster = Monster.Create("Drifter", tier: 1);
        monster.PlaceAt(new Coordinate(3, 0));
        pop.AddMonster(monster);
        var player = OffMapPlayer();

        for (var i = 0; i < 8; i++)
        {
            Tick(pop, map, player, StubRandomSource.Fixed(0.5));
        }

        Assert.Equal(new Coordinate(3, 0), monster.Position);
    }

    [Fact]
    public void Tick_ARoamingMonsterDoesStepOnALowWanderRoll()
    {
        var map = CorridorMap(8);
        var pop = EmptyPopulation(map);
        var monster = Monster.Create("Drifter", tier: 1);
        var start = new Coordinate(3, 0);
        monster.PlaceAt(start);
        pop.AddMonster(monster);

        Tick(pop, map, OffMapPlayer(), StubRandomSource.Fixed(0.0));

        Assert.NotEqual(start, monster.Position);
        Assert.True(map.Rooms.ContainsKey(monster.Position));
        Assert.Equal(1, Math.Abs(monster.Position.East - start.East) + Math.Abs(monster.Position.North - start.North));
    }

    [Fact]
    public void TickUnattended_RoamsAndInfightsButNeverAmbushes_EvenWithHostileMonsters()
    {
        var map = FourRoomMap();
        var pop = EmptyPopulation(map);

        var a = Lurker(Coordinate.Origin);
        var b = Lurker(Coordinate.Origin); // co-located → infight candidates
        a.RaiseAggro(AggroModel.Cap);
        b.RaiseAggro(AggroModel.Cap);
        pop.AddMonster(a);
        pop.AddMonster(b);
        var broadcast = new BroadcastChannel();

        // Fixed(0.0): both wander the same way (stay together) and the infight roll passes.
        MonsterController.TickUnattended(pop, map, [], year: 3210, StubRandomSource.Fixed(0.0), broadcast);

        Assert.Single(pop.Monsters.Where(m => !m.Health.IsDead));
        var slain = Assert.Single(broadcast.Events);
        Assert.Contains("was slain by", slain.Message);
        Assert.Equal(3210, slain.Year);
        Assert.DoesNotContain(broadcast.Events, e => e.Kind == GameEventKind.Ambushed);
    }

    [Fact]
    public void Tick_TwoMonstersSharingARoomCanFight_LoserDropsItsLootWhereItFell()
    {
        var map = FourRoomMap();
        var pop = EmptyPopulation(map);
        var spot = new Coordinate(1, 1);

        var bruiser = Monster.Create("Bruiser", tier: 4);
        bruiser.PlaceAt(spot);

        var trophyEntry = new LootTableEntry(Item.Create("Weakling Fang", ItemType.Weapon, 1, Rarity.Common), dropChance: 1.0);
        var weakling = new Monster("Weakling", 1, maxHp: 1, attackPower: 1, defense: 0, speed: 1, xpReward: 10, lootTable: [trophyEntry]);
        weakling.AddToInventory(Item.Create("Pocket Lint", ItemType.Junk, 1, Rarity.Common));
        weakling.PlaceAt(spot);

        pop.AddMonster(bruiser);
        pop.AddMonster(weakling);
        var broadcast = new BroadcastChannel();

        // 0.9, 0.9 -> neither wanders off; 0.1 -> the infight fires; 0.1s -> the duel.
        Tick(pop, map, OffMapPlayer(), new StubRandomSource(0.9, 0.9, 0.1), broadcast: broadcast);

        Assert.DoesNotContain(weakling, pop.Monsters);
        Assert.Contains(bruiser, pop.Monsters);
        Assert.Equal(2, pop.LootAt(spot).Count);
        // Both are monsters (common nouns) → the feed gives them articles.
        Assert.Contains(broadcast.Events, e => e.Message == "A Weakling was slain by a Bruiser.");
    }

    // --- earned aggro -----------------------------------------------------

    [Fact]
    public void Tick_ACalmMonsterIgnoresAPasserByAndNeverChases()
    {
        var map = CorridorMap(6);
        var pop = EmptyPopulation(map);

        var player = new Traveler("Prey", CharacterClass.Soldier);
        var monster = Monster.Create("Idler", tier: 1);
        monster.PlaceAt(new Coordinate(2, 0));
        pop.AddMonster(monster);
        player.PlaceAt(new Coordinate(1, 0));
        var fullHp = player.Health.Current;

        // Walk through the monster's room and out the far side.
        player.PlaceAt(new Coordinate(2, 0));
        Tick(pop, map, player, StubRandomSource.Fixed(0.99), previousPlayerPosition: new Coordinate(1, 0));
        player.PlaceAt(new Coordinate(3, 0));
        Tick(pop, map, player, StubRandomSource.Fixed(0.99), previousPlayerPosition: new Coordinate(2, 0));
        player.PlaceAt(new Coordinate(4, 0));
        Tick(pop, map, player, StubRandomSource.Fixed(0.99), previousPlayerPosition: new Coordinate(3, 0));

        Assert.Equal(AggroMood.Calm, AggroModel.MoodFor(monster.Aggro));
        Assert.Equal(fullHp, player.Health.Current);
        Assert.NotEqual(player.Position, monster.Position); // it didn't follow
    }

    [Fact]
    public void Tick_RepeatedlyEnteringAMonstersTileRampsItFromCalmToHostile()
    {
        var map = FourRoomMap();
        var pop = EmptyPopulation(map);
        var tile = Coordinate.Origin;
        var next = new Coordinate(1, 0);

        var player = new Traveler("Pest", CharacterClass.Soldier);
        var monster = Monster.Create("Guard", tier: 1);
        monster.PlaceAt(tile);
        pop.AddMonster(monster);
        player.PlaceAt(next);

        Assert.Equal(AggroMood.Calm, AggroModel.MoodFor(monster.Aggro));

        // Pace onto the tile and back off, over and over.
        for (var i = 0; i < 5; i++)
        {
            player.PlaceAt(tile);
            Tick(pop, map, player, StubRandomSource.Fixed(0.99), previousPlayerPosition: next);
            player.PlaceAt(next);
            Tick(pop, map, player, StubRandomSource.Fixed(0.99), previousPlayerPosition: tile);
        }

        Assert.Equal(AggroMood.Hostile, AggroModel.MoodFor(monster.Aggro));
    }

    [Fact]
    public void Tick_AnApexBarelyAccruesAggro_SoTheSameHarassmentThatEnragesARegularLeavesItCalm()
    {
        var map = FourRoomMap();
        var tile = Coordinate.Origin;
        var next = new Coordinate(1, 0);

        void Pace(YearPopulation pop, Traveler player)
        {
            for (var i = 0; i < 6; i++)
            {
                player.PlaceAt(tile);
                Tick(pop, map, player, StubRandomSource.Fixed(0.99), previousPlayerPosition: next);
                player.PlaceAt(next);
                Tick(pop, map, player, StubRandomSource.Fixed(0.99), previousPlayerPosition: tile);
            }
        }

        var regularPop = EmptyPopulation(map);
        var regular = Monster.Create("Guard", tier: 1);
        regular.PlaceAt(tile);
        regularPop.AddMonster(regular);
        Pace(regularPop, new Traveler("Pest", CharacterClass.Soldier));

        var apexPop = EmptyPopulation(map);
        var apex = Monster.Create("Frayed Guard", tier: 1, isApex: true);
        apex.PlaceAt(tile);
        apexPop.AddMonster(apex);
        Pace(apexPop, new Traveler("Pest", CharacterClass.Soldier));

        Assert.Equal(AggroMood.Hostile, AggroModel.MoodFor(regular.Aggro));
        Assert.Equal(AggroMood.Calm, AggroModel.MoodFor(apex.Aggro));
    }

    [Fact]
    public void Tick_AnAlertedMonsterShadowsThePlayerButTakesNoSwing()
    {
        var map = FourRoomMap();
        var pop = EmptyPopulation(map);

        var player = new Traveler("Prey", CharacterClass.Soldier);
        player.PlaceAt(new Coordinate(1, 0));
        var fullHp = player.Health.Current;

        var stalker = Monster.Create("Stalker", tier: 1);
        stalker.PlaceAt(Coordinate.Origin);
        stalker.RaiseAggro(AggroModel.AlertThreshold + 0.5); // alert, not hostile
        pop.AddMonster(stalker);

        Tick(pop, map, player, StubRandomSource.Fixed(0.99), playerLingered: true);

        Assert.Equal(player.Position, stalker.Position);   // it closed the distance
        Assert.Equal(fullHp, player.Health.Current);       // ...but an Alert monster doesn't hit
    }

    [Fact]
    public void Tick_AHostileMonsterAmbushesAnIdlePlayerAndHoldsItsTile()
    {
        var map = FourRoomMap();
        var pop = EmptyPopulation(map);

        var player = new Traveler("Prey", CharacterClass.Soldier);
        player.PlaceAt(Coordinate.Origin);
        var fullHp = player.Health.Current;

        var lurker = Lurker(Coordinate.Origin);
        lurker.RaiseAggro(AggroModel.Cap); // fully hostile
        pop.AddMonster(lurker);
        var broadcast = new BroadcastChannel();

        Tick(pop, map, player, StubRandomSource.Fixed(0.0), playerLingered: true, broadcast: broadcast);

        Assert.Equal(Coordinate.Origin, lurker.Position);
        Assert.True(player.Health.Current < fullHp);
        Assert.Contains(broadcast.Events, e => e.Kind == GameEventKind.Ambushed);
    }

    [Fact]
    public void Tick_NoAmbushFromACalmOrAlertMonsterEvenWhenIdleAndCoLocated()
    {
        var map = FourRoomMap();
        var pop = EmptyPopulation(map);

        var player = new Traveler("Prey", CharacterClass.Soldier);
        player.PlaceAt(Coordinate.Origin);
        var fullHp = player.Health.Current;

        var alert = Lurker(Coordinate.Origin);
        alert.RaiseAggro(AggroModel.AlertThreshold); // alert, below hostile
        pop.AddMonster(alert);

        Tick(pop, map, player, StubRandomSource.Fixed(0.0), playerLingered: true);

        Assert.Equal(fullHp, player.Health.Current);
    }

    [Fact]
    public void Tick_NoAmbushOnANonIdleTurn_EvenFromAHostileMonster()
    {
        var map = FourRoomMap();
        var pop = EmptyPopulation(map);

        var player = new Traveler("Prey", CharacterClass.Soldier);
        player.PlaceAt(Coordinate.Origin);
        var fullHp = player.Health.Current;

        var lurker = Lurker(Coordinate.Origin);
        lurker.RaiseAggro(AggroModel.Cap);
        pop.AddMonster(lurker);

        Tick(pop, map, player, StubRandomSource.Fixed(0.0), playerLingered: false); // acting -> safe

        Assert.Equal(fullHp, player.Health.Current);
    }

    [Fact]
    public void Tick_AmbushHasACooldown_SoIdlingHitsEveryOtherTurnNotEveryTurn()
    {
        var map = FourRoomMap();
        var pop = EmptyPopulation(map);

        var player = new Traveler("Prey", CharacterClass.Soldier);
        player.PlaceAt(Coordinate.Origin);

        var lurker = Lurker(Coordinate.Origin);
        lurker.RaiseAggro(AggroModel.Cap);
        pop.AddMonster(lurker);

        int HpDropAfterATick()
        {
            var before = player.Health.Current;
            Tick(pop, map, player, StubRandomSource.Fixed(0.0), playerLingered: true);
            return before - player.Health.Current;
        }

        Assert.True(HpDropAfterATick() > 0);
        Assert.Equal(0, HpDropAfterATick());
        Assert.True(HpDropAfterATick() > 0);
    }

    [Fact]
    public void Tick_AProvokedMonsterCalmsDownOnceThePlayerLeavesTheArea()
    {
        var map = CorridorMap(8);
        var pop = EmptyPopulation(map);

        var player = new Traveler("Prey", CharacterClass.Soldier);
        player.PlaceAt(new Coordinate(1, 0));

        var stalker = Monster.Create("Stalker", tier: 1);
        stalker.PlaceAt(Coordinate.Origin);
        stalker.RaiseAggro(AggroModel.Cap); // hostile
        pop.AddMonster(stalker);

        // Player retreats to the far end and stays there.
        for (var step = 2; step <= 7; step++)
        {
            var prev = new Coordinate(step - 1, 0);
            player.PlaceAt(new Coordinate(step, 0));
            Tick(pop, map, player, StubRandomSource.Fixed(0.99), previousPlayerPosition: prev);
        }
        for (var i = 0; i < 8; i++)
        {
            Tick(pop, map, player, StubRandomSource.Fixed(0.99));
        }

        Assert.Equal(AggroMood.Calm, AggroModel.MoodFor(stalker.Aggro));
        Assert.NotEqual(player.Position, stalker.Position);
    }

    [Fact]
    public void Tick_NoAmbushOrPursuitWhileThePlayerIsInASafeRoom()
    {
        var map = FourRoomMap();
        var pop = EmptyPopulation(map);
        var haven = Coordinate.Origin;

        var player = new Traveler("Prey", CharacterClass.Soldier);
        player.PlaceAt(haven);
        var fullHp = player.Health.Current;

        var lurker = Lurker(haven);
        lurker.RaiseAggro(AggroModel.Cap);
        pop.AddMonster(lurker);

        var adjacent = new Monster("Chaser", 1, maxHp: 30, attackPower: 8, defense: 2, speed: 8, xpReward: 40);
        adjacent.PlaceAt(new Coordinate(1, 0));
        adjacent.RaiseAggro(AggroModel.Cap);
        pop.AddMonster(adjacent);

        Tick(pop, map, player, StubRandomSource.Fixed(0.99), playerLingered: true,
            safeRooms: new HashSet<Coordinate> { haven });

        Assert.Equal(fullHp, player.Health.Current);
        Assert.NotEqual(haven, adjacent.Position);
    }

    [Fact]
    public void Tick_RespawnTrickleRefillsTowardSoftCapButNeverPastIt()
    {
        var world = TestTimeWorld.Build(seed: 314);
        var content = world.GetYear(2400);
        var pop = content.Population;
        var cap = pop.SoftCap;
        Assert.True(cap > 0);

        var player = new Traveler("Bystander", CharacterClass.Soldier);
        player.SetCurrentYear(2400);
        player.PlaceAt(new Coordinate(99, 99));

        foreach (var m in pop.Monsters.ToList())
        {
            m.Health.Damage(m.Health.Max);
            pop.RemoveMonster(m);
        }

        for (var i = 0; i < 120; i++)
        {
            Tick(pop, content.Map, player, StubRandomSource.Fixed(0.0), roster: content.MonsterRoster);
            Assert.True(pop.Monsters.Count(m => !m.Health.IsDead) <= cap, "Respawn should never overshoot the soft cap.");
        }

        Assert.True(pop.Monsters.Count(m => !m.Health.IsDead) > 0, "The trickle should refill an emptied year.");
    }
}
