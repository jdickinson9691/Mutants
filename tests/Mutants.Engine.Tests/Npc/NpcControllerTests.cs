using Mutants.Core.Characters;
using Mutants.Core.Classes;
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
}
