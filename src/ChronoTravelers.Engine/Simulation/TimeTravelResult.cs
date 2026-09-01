namespace ChronoTravelers.Engine.Simulation;

/// <summary>Outcome of a single <see cref="TimeTravelResolver.Travel"/> attempt.</summary>
public sealed record TimeTravelResult
{
    public bool Success { get; }

    /// <summary>The year travelled to, on success.</summary>
    public int? NewYear { get; }

    /// <summary>Tachyons spent on a successful jump.</summary>
    public int TachyonsSpent { get; }

    public TimeTravelFailureReason? FailureReason { get; }

    private TimeTravelResult(bool success, int? newYear, int ionsSpent, TimeTravelFailureReason? failureReason)
    {
        Success = success;
        NewYear = newYear;
        TachyonsSpent = ionsSpent;
        FailureReason = failureReason;
    }

    public static TimeTravelResult Traveled(int newYear, int ionsSpent) =>
        new(true, newYear, ionsSpent, null);

    public static TimeTravelResult Failed(TimeTravelFailureReason reason) =>
        new(false, null, 0, reason);
}
