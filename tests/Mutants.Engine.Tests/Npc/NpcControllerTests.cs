using Mutants.Core.Characters;
using Mutants.Core.Classes;
using Mutants.Core.Economy;
using Mutants.Core.Items;
using Mutants.Core.Levels;
using Mutants.Core.Monsters;
using Mutants.Core.World;
using Mutants.Engine.Npc;

namespace Mutants.Engine.Tests.Npc;

public class NpcControllerTests
{
    private static Mutant FreshNpc(CharacterClass characterClass = CharacterClass.Warrior)
    {
        var npc = new Mutant("Vex", characterClass);
        npc.PlaceAt(Coordinate.Origin);
        return npc;
    }

    /// <summary>A Warrior leveled up (and fully topped off) far enough that its Ion pool can actually afford a level-2 time-travel jump (25 * 2 = 50 Ions; a fresh level-1 Warrior only has 20 max).</summary>
    private static Mutant ReadyToTravelNpc()
    {
        var npc = FreshNpc();
        for (var i = 1; i < 20; i++)
        {
            npc.LevelUp();
        }

        npc.Health.Heal(npc.Health.Max);
        npc.Ions.Add(npc.Ions.Max);
        return npc;
    }

    [Fact]
    public void Act_DeadNpc_ReturnsIdleAndDoesNothing()
    {
        var npc = FreshNpc();
        npc.Health.Damage(npc.Health.Max);
        var level = TestLevel.Build();

        var result = NpcController.Act(npc, level, StubRandomSource.Fixed(0.5));

        Assert.Equal(NpcGoal.Idle, result.Goal);
        Assert.Equal(Coordinate.Origin, npc.Position); // never moved
    }

    [Fact]
    public void Act_LowIonsWithFodder_ConvertsAndSeeksIons()
    {
        var npc = FreshNpc();
        npc.Ions.Spend(npc.Ions.Current); // drain to 0, well under the 25% threshold
        var junk = Item.Create("Scrap", ItemType.Junk, 1, Rarity.Common);
        npc.AddToInventory(junk);
        var level = TestLevel.Build();

        var result = NpcController.Act(npc, level, StubRandomSource.Fixed(0.5));

        Assert.Equal(NpcGoal.SeekIons, result.Goal);
        Assert.DoesNotContain(junk, npc.Inventory);
        Assert.True(npc.Ions.Current > 0);
    }

    [Fact]
    public void Act_LowIonsWithNoFodder_FallsThroughToGrind()
    {
        var npc = FreshNpc();
        npc.Ions.Spend(npc.Ions.Current); // drain to 0, but inventory is empty
        var level = TestLevel.Build();

        var result = NpcController.Act(npc, level, StubRandomSource.Fixed(0.5));

        Assert.Equal(NpcGoal.Grind, result.Goal);
    }

    [Fact]
    public void Act_LowHealth_Retreats_AndDoesNotFight()
    {
        var npc = FreshNpc();
        npc.Health.Damage(npc.Health.Max - 1); // 1 HP left, well under the 30% threshold
        var level = TestLevel.Build();

        var result = NpcController.Act(npc, level, StubRandomSource.Fixed(0.5));

        Assert.Equal(NpcGoal.Retreat, result.Goal);
        Assert.Null(result.Fight);
    }

    [Fact]
    public void Act_Default_WandersAndFightsAMonster()
    {
        var npc = FreshNpc();
        var level = TestLevel.Build();

        var result = NpcController.Act(npc, level, StubRandomSource.Fixed(0.5));

        Assert.Equal(NpcGoal.Grind, result.Goal);
        Assert.NotNull(result.MonsterName);
        Assert.NotNull(result.Fight);
        Assert.NotEqual(Coordinate.Origin, npc.Position); // wandered off the start room
    }

    [Fact]
    public void Act_ExcessJunkWithAStoreAvailable_SellsOneItem()
    {
        var npc = FreshNpc();
        for (var tier = 1; tier <= 4; tier++)
        {
            npc.AddToInventory(Item.Create($"Junk Tier {tier}", ItemType.Junk, tier, Rarity.Common));
        }

        var store = Store.CreateGovernmentStore("Test Store", homeLevel: 1);
        var level = TestLevel.Build();

        var result = NpcController.Act(npc, level, StubRandomSource.Fixed(0.5), [store]);

        Assert.Equal(NpcGoal.Trade, result.Goal);
        Assert.Equal(3, npc.Inventory.Count(i => i.Type == ItemType.Junk));
        Assert.True(npc.Riblets > 0);
        Assert.Single(store.Listings); // the sold item was immediately re-listed
    }

    [Fact]
    public void Act_UnarmedWithRiblets_BuysAndWieldsAnAffordableWeapon()
    {
        var npc = FreshNpc();
        npc.AddRiblets(100);
        var store = Store.CreateGovernmentStore("Test Store", homeLevel: 1);
        var weapon = Item.Create("Cracked Shiv", ItemType.Weapon, 1, Rarity.Common);
        store.Stock(weapon, askingPrice: 50);
        var level = TestLevel.Build();

        var result = NpcController.Act(npc, level, StubRandomSource.Fixed(0.5), [store]);

        Assert.Equal(NpcGoal.Trade, result.Goal);
        Assert.Equal(weapon, npc.EquippedWeapon);
        Assert.Equal(50, npc.Riblets);
    }

    [Fact]
    public void Act_StoresAvailableButNothingToTrade_StillGrinds()
    {
        var npc = FreshNpc(); // no junk, no Riblets - nothing to sell or buy
        var store = Store.CreateGovernmentStore("Test Store", homeLevel: 1);
        var level = TestLevel.Build();

        var result = NpcController.Act(npc, level, StubRandomSource.Fixed(0.5), [store]);

        Assert.Equal(NpcGoal.Grind, result.Goal);
    }

    [Fact]
    public void Act_NoWorldGiven_NeverAttemptsTravel()
    {
        var npc = ReadyToTravelNpc();
        var level = TestLevel.Build();

        // random.NextDouble() would pass the travel-chance gate every time (0.01 < 0.10) if a world were given.
        var result = NpcController.Act(npc, level, new StubRandomSource(0.01, 0.5));

        Assert.NotEqual(NpcGoal.Travel, result.Goal);
        Assert.Equal(1, npc.CurrentTimeLevel);
    }

    [Fact]
    public void Act_TravelChanceNotRolled_FallsThroughToGrind()
    {
        var npc = ReadyToTravelNpc();
        var level = TestLevel.Build();
        var world = new GameWorld([
            new WorldLevelDefinition(1, level, TestMonsters.RosterFor(1), []),
            new WorldLevelDefinition(2, TestLevel.Build(), TestMonsters.RosterFor(2), [], minCharacterLevelToUnlock: 1),
        ]);

        var result = NpcController.Act(npc, level, StubRandomSource.Fixed(0.5), world: world); // 0.5 > the 0.10 travel-chance gate

        Assert.Equal(NpcGoal.Grind, result.Goal);
        Assert.Equal(1, npc.CurrentTimeLevel);
    }

    [Fact]
    public void Act_TravelChanceRolled_SucceedsAndUnlocksAnAlreadyReachableNextLevel()
    {
        var npc = ReadyToTravelNpc();
        var level = TestLevel.Build();
        var world = new GameWorld([
            new WorldLevelDefinition(1, level, TestMonsters.RosterFor(1), []),
            new WorldLevelDefinition(2, TestLevel.Build(), TestMonsters.RosterFor(2), [], minCharacterLevelToUnlock: 1), // no gatekeeper - a guaranteed-success jump
        ]);

        var result = NpcController.Act(npc, level, new StubRandomSource(0.01, 0.5), world: world);

        Assert.Equal(NpcGoal.Travel, result.Goal);
        Assert.Equal(2, npc.CurrentTimeLevel);
        Assert.Null(result.Fight); // no gatekeeper on this level
    }

    [Fact]
    public void Act_TravelRolledButBelowMinimumCharacterLevel_FallsThroughToGrind()
    {
        var npc = FreshNpc(); // level 1
        var level = TestLevel.Build();
        var world = new GameWorld([
            new WorldLevelDefinition(1, level, TestMonsters.RosterFor(1), []),
            new WorldLevelDefinition(2, TestLevel.Build(), TestMonsters.RosterFor(2), [], minCharacterLevelToUnlock: 20),
        ]);

        var result = NpcController.Act(npc, level, new StubRandomSource(0.01, 0.5), world: world);

        Assert.Equal(NpcGoal.Grind, result.Goal);
        Assert.Equal(1, npc.CurrentTimeLevel);
    }

    [Fact]
    public void Act_TravelRolledButCannotAffordTheIonCost_FallsThroughToGrind()
    {
        var npc = FreshNpc(); // level 1 Warrior - only 20 max Ions, well under the 50 a level-2 jump costs
        var level = TestLevel.Build();
        var world = new GameWorld([
            new WorldLevelDefinition(1, level, TestMonsters.RosterFor(1), []),
            new WorldLevelDefinition(2, TestLevel.Build(), TestMonsters.RosterFor(2), [], minCharacterLevelToUnlock: 1),
        ]);

        var result = NpcController.Act(npc, level, new StubRandomSource(0.01, 0.5), world: world);

        Assert.Equal(NpcGoal.Grind, result.Goal);
        Assert.Equal(1, npc.CurrentTimeLevel);
    }

    [Fact]
    public void Act_TravelRolledAtTheDeepestExistingLevel_FallsThroughToGrind()
    {
        var npc = ReadyToTravelNpc();
        var level = TestLevel.Build();
        var world = new GameWorld([new WorldLevelDefinition(1, level, TestMonsters.RosterFor(1), [])]); // no level 2 at all

        var result = NpcController.Act(npc, level, new StubRandomSource(0.01, 0.5), world: world);

        Assert.Equal(NpcGoal.Grind, result.Goal);
    }

    [Fact]
    public void Act_LosesAGatekeeperFight_ReportsDefeatAndStaysOnItsCurrentLevel()
    {
        var npc = ReadyToTravelNpc();
        var level = TestLevel.Build();
        var overwhelmingGatekeeper = new Monster("Overlord", tier: 2, maxHp: 500, attackPower: 500, defense: 500, speed: 1, xpReward: 0);
        var world = new GameWorld([
            new WorldLevelDefinition(1, level, TestMonsters.RosterFor(1), []),
            new WorldLevelDefinition(2, TestLevel.Build(), TestMonsters.RosterFor(2), [], gatekeeper: () => overwhelmingGatekeeper, minCharacterLevelToUnlock: 1),
        ]);

        var result = NpcController.Act(npc, level, new StubRandomSource(0.01, 0.5), world: world);

        Assert.Equal(NpcGoal.Travel, result.Goal);
        Assert.NotNull(result.Fight);
        Assert.False(result.Fight!.MutantWon);
        Assert.Equal("The Gatekeeper of Level 2", result.MonsterName);
        Assert.Equal(1, npc.CurrentTimeLevel); // never actually moved
    }

    [Fact]
    public void Act_DefaultMonsterRoster_FallsBackToTestMonstersWhenNoneIsGiven()
    {
        var npc = FreshNpc();
        var level = TestLevel.Build();

        var result = NpcController.Act(npc, level, StubRandomSource.Fixed(0.5));

        Assert.NotNull(result.MonsterName);
        Assert.Contains(TestMonsters.All, factory => factory().Name == result.MonsterName);
    }

    [Fact]
    public void Act_ExplicitMonsterRoster_FightsFromThatRosterInstead()
    {
        var npc = FreshNpc();
        var level = TestLevel.Build();
        var customRoster = TestMonsters.RosterFor(3);

        var result = NpcController.Act(npc, level, StubRandomSource.Fixed(0.5), monsterRoster: customRoster);

        Assert.NotNull(result.MonsterName);
        Assert.Contains(customRoster, factory => factory().Name == result.MonsterName);
    }
}
