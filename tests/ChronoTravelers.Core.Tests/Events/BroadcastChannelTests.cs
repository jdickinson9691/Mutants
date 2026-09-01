using ChronoTravelers.Core.Events;

namespace ChronoTravelers.Core.Tests.Events;

public class BroadcastChannelTests
{
    [Fact]
    public void Publish_AddsToEvents()
    {
        var channel = new BroadcastChannel();
        channel.Publish(GameEvent.LevelReached("Rook", 2));

        Assert.Single(channel.Events);
    }

    [Fact]
    public void Recent_ReturnsMostRecentOldestFirst()
    {
        var channel = new BroadcastChannel();
        channel.Publish(GameEvent.LevelReached("A", 1));
        channel.Publish(GameEvent.LevelReached("B", 2));
        channel.Publish(GameEvent.LevelReached("C", 3));

        var recent = channel.Recent(2);

        Assert.Equal(2, recent.Count);
        Assert.Equal("B reached level 2!", recent[0].Message);
        Assert.Equal("C reached level 3!", recent[1].Message);
    }

    [Fact]
    public void Recent_HandlesCountLargerThanHistory()
    {
        var channel = new BroadcastChannel();
        channel.Publish(GameEvent.LevelReached("A", 1));

        var recent = channel.Recent(10);

        Assert.Single(recent);
    }

    [Fact]
    public void Publish_EvictsOldestBeyondCapacity()
    {
        var channel = new BroadcastChannel(capacity: 2);
        channel.Publish(GameEvent.LevelReached("A", 1));
        channel.Publish(GameEvent.LevelReached("B", 2));
        channel.Publish(GameEvent.LevelReached("C", 3));

        Assert.Equal(2, channel.Events.Count);
        Assert.Equal("B reached level 2!", channel.Events[0].Message);
        Assert.Equal("C reached level 3!", channel.Events[1].Message);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BroadcastChannel(capacity: 0));
    }
}
