using Mutants.Core.Characters;
using Mutants.Core.Classes;
using Mutants.Core.Events;
using Mutants.Core.Items;
using Mutants.Core.Monsters;
using Mutants.Core.Time;
using Mutants.Core.World;
using Mutants.Engine.Npc;

namespace Mutants.Engine.Tests.Npc;

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

    /// <summary>An empty-roster population (no auto-placed monsters) so tests can hand-place exactly what they need.</summary>
    private static YearPopulation EmptyPopulation(LevelMap map) =>
        YearPopulation.Seed(worldSeed: 1, year: 2000, map, roster: [], gatekeeperFactory: null);

    /// <summary>A player parked well off the little test grids, so aggro/ambush never engage unless a test opts in by placing it deliberately.</summary>
    private static Mutant OffMapPlayer()
    {
        var player = new Mutant("Bystander", CharacterClass.Warrior);
        player.PlaceAt(new Coordinate(9, 9));
        return player;
    }

    [Fact]
    public void Tick_WandersALivingMonsterToAValidAdjacentRoom()
    {
        var map = FourRoomMap();
        var pop = EmptyPopulation(map);
        var monster = Monster.Create("Rover", tier: 1);
        monster.PlaceAt(Coordinate.Origin);
        pop.AddMonster(monster);

        MonsterController.Tick(pop, map, [], OffMapPlayer(), playerLingered: false, StubRandomSource.Fixed(0.0), new BroadcastChannel());

        Assert.NotEqual(Coordinate.Origin, monster.Position);
        Assert.True(map.Rooms.ContainsKey(monster.Position));
    }

    [Fact]
    public void Tick_AHurtMonsterWithIonsHeals()
    {
        var map = FourRoomMap();
        var pop = EmptyPopulation(map);
        var monster = new Monster("Bleeder", 2, maxHp: 40, attackPower: 8, defense: 3, speed: 10, xpReward: 80, maxIons: 30);
        monster.Health.Damage(30); // 10/40 HP — well under the 40% heal threshold
        monster.PlaceAt(Coordinate.Origin);
        pop.AddMonster(monster);

        MonsterController.Tick(pop, map, [], OffMapPlayer(), playerLingered: false, StubRandomSource.Fixed(0.99), new BroadcastChannel());

        Assert.True(monster.Health.Current > 10, "A hurt monster with Ions should heal.");
        Assert.True(monster.Ions.Current < 30, "Healing should spend Ions.");
    }

    [Fact]
    public void Tick_AHurtBrokeMonsterConvertsACarriedItemThenHeals()
    {
        var map = FourRoomMap();
        var pop = EmptyPopulation(map);
        var monster = new Monster("Scrounger", 2, maxHp: 40, attackPower: 8, defense: 3, speed: 10, xpReward: 80, maxIons: 30);
        monster.Health.Damage(32); // 8/40 HP
        monster.Ions.Spend(monster.Ions.Current); // broke
        monster.AddToInventory(Item.Create("Circuit Scrap", ItemType.Junk, 3, Rarity.Uncommon));
        monster.PlaceAt(Coordinate.Origin);
        pop.AddMonster(monster);

        MonsterController.Tick(pop, map, [], OffMapPlayer(), playerLingered: false, StubRandomSource.Fixed(0.99), new BroadcastChannel());

        Assert.Empty(monster.Inventory);
        Assert.True(monster.Health.Current > 8, "It should convert the scrap for Ions and heal.");
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

        // 0.9, 0.9 -> neither monster wanders off the shared room; 0.1 -> the infight fires; 0.1s -> the duel.
        MonsterController.Tick(pop, map, [], OffMapPlayer(), playerLingered: false, new StubRandomSource(0.9, 0.9, 0.1), broadcast);

        Assert.DoesNotContain(weakling, pop.Monsters);
        Assert.Contains(bruiser, pop.Monsters);
        // The carried item + the guaranteed loot-table drop both land in the room.
        Assert.Equal(2, pop.LootAt(spot).Count);
        Assert.Contains(broadcast.Events, e => e.Message == "Weakling was slain by Bruiser.");
    }

    [Fact]
    public void Tick_AnAdjacentMonsterStepsTowardThePlayerInsteadOfWandering()
    {
        var map = FourRoomMap();
        var pop = EmptyPopulation(map);

        var player = new Mutant("Prey", CharacterClass.Warrior);
        player.PlaceAt(new Coordinate(1, 0));

        var stalker = Monster.Create("Stalker", tier: 1);
        stalker.PlaceAt(Coordinate.Origin); // one room west of the player

        pop.AddMonster(stalker);

        // 0.99 would suppress a wander roll — but an aggroed monster doesn't roll, it closes.
        MonsterController.Tick(pop, map, [], player, playerLingered: false, StubRandomSource.Fixed(0.99), new BroadcastChannel());

        Assert.Equal(player.Position, stalker.Position);
    }

    [Fact]
    public void Tick_AMonsterInThePlayersRoomHoldsPositionAndLandsAnAmbushHit()
    {
        var map = FourRoomMap();
        var pop = EmptyPopulation(map);

        var player = new Mutant("Prey", CharacterClass.Warrior);
        player.PlaceAt(Coordinate.Origin);
        var fullHp = player.Health.Current;

        var lurker = new Monster("Lurker", 1, maxHp: 30, attackPower: 12, defense: 2, speed: 8, xpReward: 40);
        lurker.PlaceAt(Coordinate.Origin);
        pop.AddMonster(lurker);
        var broadcast = new BroadcastChannel();

        MonsterController.Tick(pop, map, [], player, playerLingered: true, StubRandomSource.Fixed(0.0), broadcast);

        Assert.Equal(Coordinate.Origin, lurker.Position); // held, didn't drift off
        Assert.True(player.Health.Current < fullHp, "A co-located monster should land an ambush hit.");
        Assert.Contains(broadcast.Events, e => e.Kind == GameEventKind.Ambushed);
    }

    [Fact]
    public void Tick_NoAmbushWhenNoMonsterSharesThePlayersRoom()
    {
        var map = FourRoomMap();
        var pop = EmptyPopulation(map);

        var player = new Mutant("Prey", CharacterClass.Warrior);
        player.PlaceAt(Coordinate.Origin);
        var fullHp = player.Health.Current;

        var far = Monster.Create("Far", tier: 1);
        far.PlaceAt(new Coordinate(1, 1)); // two rooms away — out of aggro range
        pop.AddMonster(far);

        MonsterController.Tick(pop, map, [], player, playerLingered: true, StubRandomSource.Fixed(0.99), new BroadcastChannel());

        Assert.Equal(fullHp, player.Health.Current);
    }

    [Fact]
    public void Tick_NoAmbushOnTheTurnThePlayerArrives_EvenSharingTheRoom()
    {
        var map = FourRoomMap();
        var pop = EmptyPopulation(map);

        var player = new Mutant("Prey", CharacterClass.Warrior);
        player.PlaceAt(Coordinate.Origin);
        var fullHp = player.Health.Current;

        var lurker = new Monster("Lurker", 1, maxHp: 30, attackPower: 12, defense: 2, speed: 8, xpReward: 40);
        lurker.PlaceAt(Coordinate.Origin);
        pop.AddMonster(lurker);

        // playerLingered: false — they just stepped in; the ambush waits a turn.
        MonsterController.Tick(pop, map, [], player, playerLingered: false, StubRandomSource.Fixed(0.0), new BroadcastChannel());

        Assert.Equal(fullHp, player.Health.Current);
    }

    [Fact]
    public void Tick_AmbushHasACooldown_SoLingeringHitsEveryOtherTurnNotEveryTurn()
    {
        var map = FourRoomMap();
        var pop = EmptyPopulation(map);

        var player = new Mutant("Prey", CharacterClass.Warrior);
        player.PlaceAt(Coordinate.Origin);

        var lurker = new Monster("Lurker", 1, maxHp: 30, attackPower: 12, defense: 2, speed: 8, xpReward: 40);
        lurker.PlaceAt(Coordinate.Origin);
        pop.AddMonster(lurker);

        int HpAfterATick()
        {
            var before = player.Health.Current;
            MonsterController.Tick(pop, map, [], player, playerLingered: true, StubRandomSource.Fixed(0.0), new BroadcastChannel());
            return before - player.Health.Current;
        }

        Assert.True(HpAfterATick() > 0, "first lingering turn: ambushed");
        Assert.Equal(0, HpAfterATick());          // cooldown turn: spared
        Assert.True(HpAfterATick() > 0, "cooldown elapsed: ambushed again");
    }

    [Fact]
    public void Tick_RespawnTrickleRefillsTowardSoftCapButNeverPastIt()
    {
        var world = TestTimeWorld.Build(seed: 314);
        var content = world.GetYear(2400);
        var pop = content.Population;
        var cap = pop.SoftCap;
        Assert.True(cap > 0);

        var player = new Mutant("Bystander", CharacterClass.Warrior);
        player.SetCurrentYear(2400);
        player.PlaceAt(new Coordinate(99, 99)); // keep aggro/ambush out of it

        // Wipe the population, then tick long enough for several respawn checks.
        foreach (var m in pop.Monsters.ToList())
        {
            m.Health.Damage(m.Health.Max);
            pop.RemoveMonster(m);
        }

        for (var i = 0; i < 120; i++)
        {
            MonsterController.Tick(pop, content.Map, content.MonsterRoster, player, playerLingered: false, StubRandomSource.Fixed(0.0), new BroadcastChannel());
            Assert.True(pop.Monsters.Count(m => !m.Health.IsDead) <= cap, "Respawn should never overshoot the soft cap.");
        }

        Assert.True(pop.Monsters.Count(m => !m.Health.IsDead) > 0, "The trickle should refill an emptied year.");
    }
}
