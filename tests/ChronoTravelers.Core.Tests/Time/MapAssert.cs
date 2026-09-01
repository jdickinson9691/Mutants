using ChronoTravelers.Core.World;

namespace ChronoTravelers.Core.Tests.Time;

/// <summary>Shared checks for generated <see cref="LevelMap"/>s.</summary>
internal static class MapAssert
{
    /// <summary>Every room reachable from Start by walking declared exits.</summary>
    public static HashSet<Coordinate> RoomsReachableFromStart(LevelMap map)
    {
        var visited = new HashSet<Coordinate> { map.Start };
        var frontier = new Queue<Coordinate>();
        frontier.Enqueue(map.Start);

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            foreach (var direction in map.GetRoom(current).ExitDescriptions.Keys)
            {
                var next = current.Move(direction);
                if (visited.Add(next))
                {
                    frontier.Enqueue(next);
                }
            }
        }

        return visited;
    }

    public static void IsWellFormedAndFullyConnected(LevelMap map)
    {
        Assert.Empty(map.Validate());
        Assert.Equal(map.RoomCount, RoomsReachableFromStart(map).Count);
    }
}
