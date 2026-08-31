using ChronTravelers.Core.Events;

namespace ChronTravelers.Core.Tests.Events;

public class GameEventTests
{
    [Fact]
    public void Slain_FormatsVictimAndKiller()
    {
        var evt = GameEvent.Slain("Rook", "Feral Dog");
        Assert.Equal("Rook was slain by Feral Dog.", evt.Message);
    }

    [Fact]
    public void LevelReached_FormatsNameAndLevel()
    {
        var evt = GameEvent.LevelReached("Rook", 5);
        Assert.Equal("Rook reached level 5!", evt.Message);
    }

    [Fact]
    public void Events_CarryAnOptionalYear_ForTheInlineFeedFilter()
    {
        Assert.Null(GameEvent.Slain("A", "B").Year);
        Assert.Equal(3200, GameEvent.Slain("A", "B", 3200).Year);
        Assert.Equal(4000, GameEvent.Ambushed("Beast", "Rook", 7, 4000).Year);
        Assert.Equal(2500, GameEvent.LevelReached("Rook", 9, 2500).Year);
        Assert.Equal(3400, GameEvent.TimeTraveled("Rook", 3400).Year);
    }
}
