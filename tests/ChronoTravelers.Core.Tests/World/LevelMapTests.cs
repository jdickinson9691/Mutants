using ChronoTravelers.Core.World;

namespace ChronoTravelers.Core.Tests.World;

/// <summary>docs/GDD.md item #5's Engineer "Jump Rig" — see <see cref="LevelMap.RoomsWithinHops"/>.</summary>
public class LevelMapTests
{
    // A straight 5-room east corridor: (0,0)-(1,0)-(2,0)-(3,0)-(4,0).
    private static LevelMap Corridor()
    {
        var rooms = new Dictionary<Coordinate, Room>();
        for (var e = 0; e <= 4; e++)
        {
            var coordinate = new Coordinate(e, 0);
            var exits = new List<(Direction, string)>();
            if (e > 0)
            {
                exits.Add((Direction.West, "back"));
            }

            if (e < 4)
            {
                exits.Add((Direction.East, "onward"));
            }

            rooms[coordinate] = Room.Create($"Room {e}", exits.ToArray());
        }

        return new LevelMap("Corridor", Coordinate.Origin, rooms);
    }

    [Fact]
    public void RoomsWithinHops_ExcludesOrigin()
    {
        var map = Corridor();
        Assert.DoesNotContain(Coordinate.Origin, map.RoomsWithinHops(Coordinate.Origin, maxHops: 3));
    }

    [Fact]
    public void RoomsWithinHops_ReturnsOnlyRoomsWithinRange()
    {
        var map = Corridor();
        var within2 = map.RoomsWithinHops(Coordinate.Origin, maxHops: 2);

        Assert.Contains(new Coordinate(1, 0), within2);
        Assert.Contains(new Coordinate(2, 0), within2);
        Assert.DoesNotContain(new Coordinate(3, 0), within2);
        Assert.DoesNotContain(new Coordinate(4, 0), within2);
    }

    [Fact]
    public void RoomsWithinHops_LargerRadiusReachesFurther()
    {
        var map = Corridor();
        var within4 = map.RoomsWithinHops(Coordinate.Origin, maxHops: 4);

        Assert.Equal(4, within4.Count);
        Assert.Contains(new Coordinate(4, 0), within4);
    }

    [Fact]
    public void RoomsWithinHops_OriginNotOnMap_ReturnsEmpty()
    {
        var map = Corridor();
        Assert.Empty(map.RoomsWithinHops(new Coordinate(99, 99), maxHops: 3));
    }

    [Fact]
    public void RoomsWithinHops_SingleRoomMap_ReturnsEmpty()
    {
        var rooms = new Dictionary<Coordinate, Room> { [Coordinate.Origin] = Room.Create("Alone") };
        var map = new LevelMap("Solo", Coordinate.Origin, rooms);

        Assert.Empty(map.RoomsWithinHops(Coordinate.Origin, maxHops: 3));
    }
}
