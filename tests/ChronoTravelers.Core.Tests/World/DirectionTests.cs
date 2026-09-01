using ChronoTravelers.Core.World;

namespace ChronoTravelers.Core.Tests.World;

public class DirectionTests
{
    [Theory]
    [InlineData(Direction.North, Direction.South)]
    [InlineData(Direction.South, Direction.North)]
    [InlineData(Direction.East, Direction.West)]
    [InlineData(Direction.West, Direction.East)]
    public void Opposite_ReturnsReverseDirection(Direction direction, Direction expected)
    {
        Assert.Equal(expected, direction.Opposite());
    }

    [Theory]
    [InlineData(Direction.North, "n")]
    [InlineData(Direction.South, "s")]
    [InlineData(Direction.East, "e")]
    [InlineData(Direction.West, "w")]
    public void Command_IsSingleLetterPerGdd(Direction direction, string expected)
    {
        Assert.Equal(expected, direction.Command());
    }

    [Theory]
    [InlineData("n", Direction.North)]
    [InlineData("N", Direction.North)]
    [InlineData("north", Direction.North)]
    [InlineData(" North ", Direction.North)]
    [InlineData("s", Direction.South)]
    [InlineData("e", Direction.East)]
    [InlineData("w", Direction.West)]
    public void Parse_AcceptsLetterAndFullWordCaseInsensitively(string input, Direction expected)
    {
        Assert.Equal(expected, DirectionExtensions.Parse(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("northeast")]
    [InlineData("up")]
    public void Parse_ReturnsNullForUnrecognizedInput(string input)
    {
        Assert.Null(DirectionExtensions.Parse(input));
    }
}
