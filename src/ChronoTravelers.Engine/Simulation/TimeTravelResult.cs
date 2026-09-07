namespace ChronoTravelers.Engine.Simulation;

/// <summary>Outcome of a single <see cref="TimeTravelResolver.Travel"/> attempt.</summary>
public sealed record TimeTravelResult
{
    public bool Success { get; }

    /// <summary>The year travelled to, on success. Null for a <see cref="Charging"/> result — arrival hasn't happened yet.</summary>
    public int? NewYear { get; }

    /// <summary>Tachyons spent on a successful jump (also charged up front for a jump that starts <see cref="Charging"/> — see docs/ENDGAME_STRATEGY.md recommendation 5).</summary>
    public int TachyonsSpent { get; }

    public TimeTravelFailureReason? FailureReason { get; }

    /// <summary>
    /// True if this jump exceeded <see cref="Characters.Traveler.ChargeTravelThresholdYears"/>
    /// and is now charging rather than resolving immediately
    /// (docs/ENDGAME_STRATEGY.md recommendation 5) — <see cref="Success"/>
    /// is still true (the jump was accepted and Tachyons were spent), but
    /// <see cref="NewYear"/> stays null until
    /// <c>Engine.Simulation.WorldSimulation</c> completes the charge over
    /// several ticks. Additive: every pre-existing caller that only checks
    /// <see cref="Success"/>/<see cref="NewYear"/>/<see cref="FailureReason"/>
    /// keeps working (a charging jump just never sets <see cref="NewYear"/>
    /// on the tick it starts). Named <c>IsCharging</c> rather than
    /// <c>Charging</c> so it doesn't collide with the <see cref="Charging"/>
    /// factory method below — C# doesn't allow a property and a method to
    /// share one name.
    /// </summary>
    public bool IsCharging { get; }

    /// <summary>The year an <see cref="IsCharging"/> jump is headed to, or null.</summary>
    public int? ChargingTargetYear { get; }

    /// <summary>Total ticks an <see cref="IsCharging"/> jump needs to complete, or null.</summary>
    public int? ChargingTicksRequired { get; }

    private TimeTravelResult(bool success, int? newYear, int ionsSpent, TimeTravelFailureReason? failureReason, bool charging, int? chargingTargetYear, int? chargingTicksRequired)
    {
        Success = success;
        NewYear = newYear;
        TachyonsSpent = ionsSpent;
        FailureReason = failureReason;
        IsCharging = charging;
        ChargingTargetYear = chargingTargetYear;
        ChargingTicksRequired = chargingTicksRequired;
    }

    public static TimeTravelResult Traveled(int newYear, int ionsSpent) =>
        new(true, newYear, ionsSpent, null, false, null, null);

    public static TimeTravelResult Failed(TimeTravelFailureReason reason) =>
        new(false, null, 0, reason, false, null, null);

    /// <summary>
    /// A long-distance jump has started charging (docs/ENDGAME_STRATEGY.md
    /// recommendation 5), or an already-in-flight charge toward the same
    /// target is being reported back as a no-op status query — see
    /// <see cref="TimeTravelResolver.Travel"/>.
    /// </summary>
    public static TimeTravelResult Charging(int targetYear, int ticksRequired, int ionsSpent = 0) =>
        new(true, null, ionsSpent, null, true, targetYear, ticksRequired);
}
