using Mutants.Core.World;

namespace Mutants.Core.Tests.World;

public class LevelMapTests
{
    private static LevelMap TwoRoomMap()
    {
        var start = Coordinate.Origin;
        var east = start.Move(Direction.East);

        var rooms = new Dictionary<Coordinate, Room>
        {
            [start] = Room.Create("A quiet courtyard.", (Direction.East, "area continues.")),
            [east] = Room.Create("A narrow alley.", (Direction.West, "back to the courtyard.")),
        };

        return new LevelMap("Two-Room Test", start, rooms);
    }

    [Fact]
    public void Constructor_RejectsEmptyRoomSet()
    {
        Assert.Throws<ArgumentException>(() =>
            new LevelMap("Empty", Coordinate.Origin, new Dictionary<Coordinate, Room>()));
    }

    [Fact]
    public void Constructor_RejectsStartCoordinateWithNoRoom()
    {
        var rooms = new Dictionary<Coordinate, Room>
        {
            [new Coordinate(5, 5)] = Room.Create("Somewhere else."),
        };

        Assert.Throws<ArgumentException>(() => new LevelMap("Bad Start", Coordinate.Origin, rooms));
    }

    [Fact]
    public void TryGetRoom_ReturnsNullOutsideTheMap()
    {
        var map = TwoRoomMap();
        Assert.Null(map.TryGetRoom(new Coordinate(99, 99)));
    }

    [Fact]
    public void TryMove_SucceedsAcrossAnAuthoredExit()
    {
        var map = TwoRoomMap();
        var result = map.TryMove(Coordinate.Origin, Direction.East);

        Assert.True(result.Success);
        Assert.Equal(Coordinate.Origin.Move(Direction.East), result.Destination);
        Assert.Equal("A narrow alley.", result.DestinationRoom!.Description);
    }

    [Fact]
    public void TryMove_BlockedWhenRoomHasNoExitThatWay()
    {
        var map = TwoRoomMap();
        var result = map.TryMove(Coordinate.Origin, Direction.North);

        Assert.False(result.Success);
        Assert.Equal(MoveFailureReason.NoExit, result.FailureReason);
    }

    [Fact]
    public void TryMove_BlockedWhenExitPointsToAMissingRoom()
    {
        var brokenExitRoom = Room.Create("A doorway to nowhere.", (Direction.North, "a gap in reality."));
        var rooms = new Dictionary<Coordinate, Room> { [Coordinate.Origin] = brokenExitRoom };
        var map = new LevelMap("Broken", Coordinate.Origin, rooms);

        var result = map.TryMove(Coordinate.Origin, Direction.North);

        Assert.False(result.Success);
        Assert.Equal(MoveFailureReason.NoRoomBeyondExit, result.FailureReason);
    }

    [Fact]
    public void Validate_ReturnsEmptyForAWellFormedLevel()
    {
        Assert.Empty(TwoRoomMap().Validate());
    }

    [Fact]
    public void Validate_ReportsExitsThatLeadNowhere()
    {
        var brokenExitRoom = Room.Create("A doorway to nowhere.", (Direction.North, "a gap in reality."));
        var rooms = new Dictionary<Coordinate, Room> { [Coordinate.Origin] = brokenExitRoom };
        var map = new LevelMap("Broken", Coordinate.Origin, rooms);

        var problems = map.Validate();

        Assert.Single(problems);
        Assert.Contains("north", problems[0]);
    }
}
