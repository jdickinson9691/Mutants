using ChronTravelers.Core.Characters;
using ChronTravelers.Core.Classes;
using ChronTravelers.Core.Economy;
using ChronTravelers.Core.Items;
using ChronTravelers.Core.Monsters;
using ChronTravelers.Core.Time;
using ChronTravelers.Core.World;
using ChronTravelers.Engine.Npc;

namespace ChronTravelers.Engine.Tests.Npc;

public class NpcControllerTests
{
    private static readonly LevelMap TestLevelMap = TestLevel.Build();

    private static Mutant FreshNpc(CharacterClass characterClass = CharacterClass.Warrior, int year = 2000)
    {
        var npc = new Mutant("Vex", characterClass, startingYear: year);
        npc.PlaceAt(Coordinate.Origin);
        return npc;
    }

    /// <summary>A Warrior levelled far enough (and topped off) that its Ion pool can afford a year-hop.</summary>
    private static Mutant ReadyToTravelNpc(int year = 2000)
    {
        var npc = FreshNpc(year: year);
        for (var i = 1; i < 20; i++)
        {
            npc.LevelUp();
        }

        npc.Health.Heal(npc.Health.Max);
        npc.Ions.SetMax(500);
        npc.Ions.Add(500);
        return npc;
    }

    [Fact]
    public void Act_DeadNpc_ReturnsIdleAndDoesNothing()
    {
        var npc = FreshNpc();
        npc.Health.Damage(npc.Health.Max);

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5));

        Assert.Equal(NpcGoal.Idle, result.Goal);
        Assert.Equal(Coordinate.Origin, npc.Position);
    }

    [Fact]
    public void Act_LowIonsWithFodder_ConvertsAndSeeksIons()
    {
        var npc = FreshNpc();
        npc.Ions.Spend(npc.Ions.Current);
        var junk = Item.Create("Scrap", ItemType.Junk, 1, Rarity.Common);
        npc.AddToInventory(junk);

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5));

        Assert.Equal(NpcGoal.SeekIons, result.Goal);
        Assert.DoesNotContain(junk, npc.Inventory);
        Assert.True(npc.Ions.Current > 0);
    }

    [Fact]
    public void Act_LowIonsWithNoFodder_FallsThroughToGrind()
    {
        var npc = FreshNpc();
        npc.Ions.Spend(npc.Ions.Current);

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5));

        Assert.Equal(NpcGoal.Grind, result.Goal);
    }

    [Fact]
    public void Act_LowHealth_Retreats_AndDoesNotFight()
    {
        var npc = FreshNpc();
        npc.Health.Damage(npc.Health.Max - 1);

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5));

        Assert.Equal(NpcGoal.Retreat, result.Goal);
        Assert.Null(result.Fight);
    }

    [Fact]
    public void Act_Default_WandersAndFightsAMonster()
    {
        var npc = FreshNpc();

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5));

        Assert.Equal(NpcGoal.Grind, result.Goal);
        Assert.NotNull(result.MonsterName);
        Assert.NotNull(result.Fight);
        Assert.NotEqual(Coordinate.Origin, npc.Position);
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

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5), [store]);

        Assert.Equal(NpcGoal.Trade, result.Goal);
        Assert.Equal(3, npc.Inventory.Count(i => i.Type == ItemType.Junk));
        Assert.True(npc.Riblets > 0);
        Assert.Single(store.Listings);
    }

    [Fact]
    public void Act_UnarmedWithRiblets_BuysAndWieldsAnAffordableWeapon()
    {
        var npc = FreshNpc();
        npc.AddRiblets(100);
        var store = Store.CreateGovernmentStore("Test Store", homeLevel: 1);
        var weapon = Item.Create("Cracked Shiv", ItemType.Weapon, 1, Rarity.Common);
        store.Stock(weapon, askingPrice: 50);

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5), [store]);

        Assert.Equal(NpcGoal.Trade, result.Goal);
        Assert.Equal(weapon, npc.EquippedWeapon);
        Assert.Equal(50, npc.Riblets);
    }

    [Fact]
    public void Act_StoresAvailableButNothingToTrade_StillGrinds()
    {
        var npc = FreshNpc();
        var store = Store.CreateGovernmentStore("Test Store", homeLevel: 1);

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5), [store]);

        Assert.Equal(NpcGoal.Grind, result.Goal);
    }

    [Fact]
    public void Act_NoWorldGiven_NeverAttemptsTravel()
    {
        var npc = ReadyToTravelNpc();

        // 0.01 would pass the travel-chance gate if a world were given.
        var result = NpcController.Act(npc, TestLevelMap, new StubRandomSource(0.01, 0.5));

        Assert.NotEqual(NpcGoal.Travel, result.Goal);
        Assert.Equal(2000, npc.CurrentYear);
    }

    [Fact]
    public void Act_TravelChanceNotRolled_FallsThroughToGrind()
    {
        var npc = ReadyToTravelNpc();
        var world = TestTimeWorld.Build();

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5), world: world); // 0.5 > the 0.10 gate

        Assert.Equal(NpcGoal.Grind, result.Goal);
        Assert.Equal(2000, npc.CurrentYear);
    }

    [Fact]
    public void Act_TravelChanceRolled_JumpsForwardAlongTheTimeline()
    {
        var npc = ReadyToTravelNpc();
        var world = TestTimeWorld.Build();

        // 0.01 passes the gate; 0.5 -> a mid-range hop; 0.5 -> forward (< 0.8 bias).
        var result = NpcController.Act(npc, TestLevelMap, new StubRandomSource(0.01, 0.5, 0.5, 0.5), world: world);

        Assert.Equal(NpcGoal.Travel, result.Goal);
        Assert.True(npc.CurrentYear > 2000);
        Assert.InRange(npc.CurrentYear, 2001, TimeScale.MaxYear);
    }

    [Fact]
    public void Act_TravelRolledButCannotAffordTheIonCost_FallsThroughToGrind()
    {
        var npc = FreshNpc(); // level 1 Warrior — ~20 max Ions
        npc.Ions.Spend(npc.Ions.Current); // 0 Ions — can't afford any hop
        var world = TestTimeWorld.Build();

        var result = NpcController.Act(npc, TestLevelMap, new StubRandomSource(0.01, 0.5, 0.5, 0.5), world: world);

        Assert.NotEqual(NpcGoal.Travel, result.Goal);
        Assert.Equal(2000, npc.CurrentYear);
    }

    [Fact]
    public void Act_TravelRolledAtTheEndOfTheTimeline_StaysPutAndGrinds()
    {
        var npc = ReadyToTravelNpc(year: TimeScale.MaxYear);
        var world = TestTimeWorld.Build();

        // Gate passes, hop forward — but there's nowhere forward from 5000, so it clamps to the same year and bails.
        var result = NpcController.Act(npc, TestLevelMap, new StubRandomSource(0.01, 0.5, 0.5, 0.5), world: world);

        Assert.Equal(NpcGoal.Grind, result.Goal);
        Assert.Equal(TimeScale.MaxYear, npc.CurrentYear);
    }

    [Fact]
    public void Act_DefaultMonsterRoster_FallsBackToTestMonstersWhenNoneIsGiven()
    {
        var npc = FreshNpc();

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5));

        Assert.NotNull(result.MonsterName);
        Assert.Contains(TestMonsters.All, factory => factory().Name == result.MonsterName);
    }

    [Fact]
    public void Act_ExplicitMonsterRoster_FightsFromThatRosterInstead()
    {
        var npc = FreshNpc();
        var customRoster = TestMonsters.RosterFor(3);

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5), monsterRoster: customRoster);

        Assert.NotNull(result.MonsterName);
        Assert.Contains(customRoster, factory => factory().Name == result.MonsterName);
    }
}
