using ChronoTravelers.Core.World;

namespace ChronoTravelers.Core.Tests.World;

public class TestLevelTests
{
    [Fact]
    public void Build_ProducesAWellFormedLevel()
    {
        var level = TestLevel.Build();
        Assert.Empty(level.Validate());
    }

    [Fact]
    public void Build_StartRoomIsTheOrigin()
    {
        var level = TestLevel.Build();
        Assert.Equal(Coordinate.Origin, level.Start);
        Assert.NotNull(level.TryGetRoom(Coordinate.Origin));
    }

    [Fact]
    public void Build_AllNineGridRoomsAreReachableFromStart()
    {
        var level = TestLevel.Build();
        var visited = new HashSet<Coordinate>();
        var frontier = new Queue<Coordinate>();
        frontier.Enqueue(level.Start);
        visited.Add(level.Start);

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            var room = level.GetRoom(current);
            foreach (var direction in room.ExitDescriptions.Keys)
            {
                var next = current.Move(direction);
                if (visited.Add(next))
                {
                    frontier.Enqueue(next);
                }
            }
        }

        Assert.Equal(9, visited.Count);
    }

    [Fact]
    public void Build_ExitsAreMutual_WalkingThroughAndBackReturnsToStart()
    {
        var level = TestLevel.Build();

        var toEast = level.TryMove(level.Start, Direction.East);
        Assert.True(toEast.Success);

        var backWest = level.TryMove(toEast.Destination!.Value, Direction.West);
        Assert.True(backWest.Success);
        Assert.Equal(level.Start, backWest.Destination);
    }
}
