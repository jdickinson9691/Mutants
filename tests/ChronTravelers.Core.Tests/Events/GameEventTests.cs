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
}
