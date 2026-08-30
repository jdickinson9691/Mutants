using Mutants.Core.Characters;
using Mutants.Core.Classes;
using Mutants.Core.Economy;
using Mutants.Core.Items;
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
}
