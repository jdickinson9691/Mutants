using ChronoTravelers.Core.World;
using ChronoTravelers.Engine.Combat;

namespace ChronoTravelers.Engine.Tests.Combat;

public class RangedTargetingTests
{
    // TestLevel.Build() is a fully-connected 3x3 grid on (-1..1, -1..1) —
    // exits exist only between two cells that both have content, so firing
    // off the 3x3 edge hits a real "corridor breaks" case for free.
    private static readonly LevelMap Map = TestLevel.Build();

    [Fact]
    public void HasClearShot_TargetOneRoomAway_ReturnsTrue()
    {
        var found = RangedTargeting.HasClearShot(Map, Coordinate.Origin, Direction.East, range: 1, targetPosition: new Coordinate(1, 0));

        Assert.True(found);
    }

    [Fact]
    public void HasClearShot_TargetBeyondWeaponRange_ReturnsFalse()
    {
        // (-1,0) -> (1,0) east is 2 rooms; a range-1 weapon can't reach it.
        var found = RangedTargeting.HasClearShot(Map, new Coordinate(-1, 0), Direction.East, range: 1, targetPosition: new Coordinate(1, 0));

        Assert.False(found);
    }

    [Fact]
    public void HasClearShot_TargetExactlyAtWeaponRange_ReturnsTrue()
    {
        var found = RangedTargeting.HasClearShot(Map, new Coordinate(-1, 0), Direction.East, range: 2, targetPosition: new Coordinate(1, 0));

        Assert.True(found);
    }

    [Fact]
    public void HasClearShot_ExtraRangePastTheTarget_StillReturnsTrue()
    {
        // A weapon whose range exceeds the distance still finds the target
        // along the way — it doesn't have to land exactly at max range.
        var found = RangedTargeting.HasClearShot(Map, Coordinate.Origin, Direction.East, range: 4, targetPosition: new Coordinate(1, 0));

        Assert.True(found);
    }

    [Fact]
    public void HasClearShot_TargetInADifferentDirection_ReturnsFalse()
    {
        // Target is north, but firing east.
        var found = RangedTargeting.HasClearShot(Map, Coordinate.Origin, Direction.East, range: 2, targetPosition: new Coordinate(0, 1));

        Assert.False(found);
    }

    [Fact]
    public void HasClearShot_CorridorBreaksAtTheMapEdge_ReturnsFalseAndNeverReachesTheTarget()
    {
        // Firing east from the grid's own east edge — no exit that way at
        // all, so the walk stops on the very first step regardless of range.
        var found = RangedTargeting.HasClearShot(Map, new Coordinate(1, 0), Direction.East, range: 3, targetPosition: new Coordinate(2, 0));

        Assert.False(found);
    }

    [Fact]
    public void HasClearShot_TargetSharesTheShootersRoom_ReturnsFalse()
    {
        // Zero steps taken means zero chance to match — a shooter never
        // "shoots" its own room via this path (RangedResolver.Fire handles
        // the point-blank case separately, on the caller's side).
        var found = RangedTargeting.HasClearShot(Map, Coordinate.Origin, Direction.East, range: 2, targetPosition: Coordinate.Origin);

        Assert.False(found);
    }

    [Fact]
    public void FindClearShotDirection_ReturnsTheDirectionWithALine()
    {
        var direction = RangedTargeting.FindClearShotDirection(Map, Coordinate.Origin, range: 1, targetPosition: new Coordinate(0, 1));

        Assert.Equal(Direction.North, direction);
    }

    [Fact]
    public void FindClearShotDirection_NoDirectionHasALine_ReturnsNull()
    {
        // Diagonally offset — no single cardinal direction's straight
        // corridor ever passes through (1,1) from the origin.
        var direction = RangedTargeting.FindClearShotDirection(Map, Coordinate.Origin, range: 2, targetPosition: new Coordinate(1, 1));

        Assert.Null(direction);
    }
}
