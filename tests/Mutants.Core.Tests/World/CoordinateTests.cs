using Mutants.Core.World;

namespace Mutants.Core.Tests.World;

public class CoordinateTests
{
    [Theory]
    [InlineData(Direction.North, 0, 1)]
    [InlineData(Direction.South, 0, -1)]
    [InlineData(Direction.East, 1, 0)]
    [InlineData(Direction.West, -1, 0)]
    public void Move_OffsetsCorrectAxis(Direction direction, int expectedEast, int expectedNorth)
    {
        var moved = Coordinate.Origin.Move(direction);

        Assert.Equal(expectedEast, moved.East);
        Assert.Equal(expectedNorth, moved.North);
    }

    [Fact]
    public void Move_ThenOpposite_ReturnsToStart()
    {
        var start = new Coordinate(2, -1);
        var roundTrip = start.Move(Direction.East).Move(Direction.East.Opposite());

        Assert.Equal(start, roundTrip);
    }

    [Fact]
    public void ToString_MatchesSourceGameCompassFormat()
    {
        var coordinate = new Coordinate(2, 0);
        Assert.Equal("(2E : 0N)", coordinate.ToString());
    }

    [Fact]
    public void Equality_IsValueBased()
    {
        Assert.Equal(new Coordinate(3, -2), new Coordinate(3, -2));
        Assert.NotEqual(new Coordinate(3, -2), new Coordinate(-2, 3));
    }
}
