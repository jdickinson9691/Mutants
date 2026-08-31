namespace ChronTravelers.Core.World;

public enum MoveFailureReason
{
    /// <summary>The current room has no authored exit in that direction.</summary>
    NoExit,

    /// <summary>The room has an authored exit, but no room is registered on the far side (a level-authoring bug).</summary>
    NoRoomBeyondExit,
}

/// <summary>Outcome of attempting to move from one room to an adjacent one.</summary>
public sealed record MoveResult
{
    public bool Success { get; }
    public Coordinate? Destination { get; }
    public Room? DestinationRoom { get; }
    public MoveFailureReason? FailureReason { get; }

    private MoveResult(bool success, Coordinate? destination, Room? destinationRoom, MoveFailureReason? failureReason)
    {
        Success = success;
        Destination = destination;
        DestinationRoom = destinationRoom;
        FailureReason = failureReason;
    }

    public static MoveResult Moved(Coordinate destination, Room destinationRoom) =>
        new(true, destination, destinationRoom, null);

    public static MoveResult Blocked(MoveFailureReason reason) =>
        new(false, null, null, reason);
}
