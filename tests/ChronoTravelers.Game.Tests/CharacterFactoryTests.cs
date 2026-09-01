using ChronoTravelers.Core.Classes;
using ChronoTravelers.Engine.Persistence;
using ChronoTravelers.Game;

namespace ChronoTravelers.Game.Tests;

public class CharacterFactoryTests
{
    private static CharacterSaveData Saved(CharacterClass c) => new() { Name = c.ToString(), Class = c.ToString() };

    [Fact]
    public void OfferedClasses_ExcludesRolesTheAccountHasAlreadyPlayed()
    {
        var offered = CharacterFactory.OfferedClasses([Saved(CharacterClass.Soldier), Saved(CharacterClass.Doctor)]);

        Assert.DoesNotContain(CharacterClass.Soldier, offered);
        Assert.DoesNotContain(CharacterClass.Doctor, offered);
        Assert.Contains(CharacterClass.Spy, offered);
        Assert.Equal(3, offered.Count);
    }

    [Fact]
    public void OfferedClasses_FallsBackToAllFiveWhenEveryRoleHasBeenPlayed()
    {
        var all = Enum.GetValues<CharacterClass>().Select(Saved).ToList();
        var offered = CharacterFactory.OfferedClasses(all);
        Assert.Equal(5, offered.Count);
    }

    [Fact]
    public void NewTraveler_HasTheStarterKit()
    {
        var t = CharacterFactory.NewTraveler("Rook", CharacterClass.Engineer);
        Assert.Equal(CharacterClass.Engineer, t.Class);
        Assert.Equal(3, t.Inventory.Count(i => i.Name == "Field Ration"));
    }
}
