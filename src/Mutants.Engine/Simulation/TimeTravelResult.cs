using Mutants.Engine.Combat;

namespace Mutants.Engine.Simulation;

/// <summary>Outcome of a single <see cref="TimeTravelResolver.Travel"/> attempt.</summary>
public sealed record TimeTravelResult
{
    public bool Success { get; }
    public int? NewLevel { get; }
    public TimeTravelFailureReason? FailureReason { get; }

    /// <summary>Set if a gatekeeper fight happened during this attempt (win or lose).</summary>
    public FightResult? GatekeeperFight { get; }

    private TimeTravelResult(bool success, int? newLevel, TimeTravelFailureReason? failureReason, FightResult? gatekeeperFight)
    {
        Success = success;
        NewLevel = newLevel;
        FailureReason = failureReason;
        GatekeeperFight = gatekeeperFight;
    }

    public static TimeTravelResult Traveled(int newLevel, FightResult? gatekeeperFight = null) =>
        new(true, newLevel, null, gatekeeperFight);

    public static TimeTravelResult Failed(TimeTravelFailureReason reason, FightResult? gatekeeperFight = null) =>
        new(false, null, reason, gatekeeperFight);
}
