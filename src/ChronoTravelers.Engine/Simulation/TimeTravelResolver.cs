using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Tachyons;
using ChronoTravelers.Core.Time;

namespace ChronoTravelers.Engine.Simulation;

/// <summary>
/// Resolves a <c>travel</c> attempt on the continuous timeline —
/// docs/GDD.md §3.2. Travel is unrestricted: any year in
/// 2000–5000 you can afford. The only checks are that the year is on the
/// timeline and that the Traveler can pay
/// <see cref="TachyonEconomy.TimeTravelCost"/> (symmetric — retreating costs
/// the same as advancing). No unlock, no minimum character level, no
/// Warden gate; Wardens are just tough encounters in their year,
/// handled by the fight flow, not here.
///
/// <b>Charged jumps</b> (docs/ENDGAME_STRATEGY.md recommendation 5): a jump
/// of <see cref="Traveler.ChargeTravelThresholdYears"/> years or more
/// still costs the same Tachyons, paid up front here, but doesn't arrive
/// immediately — it starts charging (<see cref="Traveler.BeginChargingTravel"/>)
/// and <c>Engine.Simulation.WorldSimulation</c>'s per-tick
/// <see cref="Traveler.AdvancePendingTravel"/> call completes it several
/// ticks later. Calling <see cref="Travel"/> again toward the SAME target
/// while already charging is a no-op status query (no re-charge, no double
/// spend); toward a DIFFERENT target it cancels the old charge — no refund,
/// the same "you committed and changed your mind" rule as any other spent
/// Tachyons — and starts the new one. NPCs can never reach this distance
/// (<c>Engine.Npc.NpcController.MaxTravelHop</c> tops out at 300, 450 for a
/// Wanderer), so this branch is only ever reachable from a player-initiated
/// jump.
/// </summary>
public static class TimeTravelResolver
{
    /// <summary>
    /// <paramref name="random"/> is unused now (kept so callers that
    /// thread an <see cref="IRandomSource"/> don't have to change) — there
    /// is no Warden fight during travel any more.
    /// </summary>
    public static TimeTravelResult Travel(Traveler traveler, TimeWorld world, int targetYear, IRandomSource random)
    {
        _ = random;

        if (!TimeScale.IsValidYear(targetYear))
        {
            return TimeTravelResult.Failed(TimeTravelFailureReason.YearOutOfRange);
        }

        // Already charging toward this exact target — report the same
        // charging state back rather than restarting or double-spending.
        if (traveler.IsChargingTravel && traveler.ChargingTargetYear == targetYear)
        {
            return TimeTravelResult.Charging(targetYear, traveler.ChargingTicksRequired!.Value);
        }

        var cost = TachyonEconomy.TimeTravelCost(traveler.CurrentYear, targetYear);
        if (cost > 0 && !traveler.Tachyons.CanAfford(cost))
        {
            return TimeTravelResult.Failed(TimeTravelFailureReason.InsufficientTachyons);
        }

        if (cost > 0)
        {
            traveler.Tachyons.Spend(cost);
        }

        var distance = Math.Abs(targetYear - traveler.CurrentYear);
        if (distance >= Traveler.ChargeTravelThresholdYears)
        {
            // A different target while already charging cancels the old
            // charge (no refund — see this class's doc comment) and starts
            // the new one under the same rule.
            if (traveler.IsChargingTravel)
            {
                traveler.CancelPendingTravel();
            }

            var ticksRequired = Traveler.TicksRequiredForChargedTravel(distance);
            traveler.BeginChargingTravel(targetYear, ticksRequired);
            return TimeTravelResult.Charging(targetYear, ticksRequired, cost);
        }

        traveler.SetCurrentYear(targetYear);
        traveler.PlaceAt(world.GetYear(targetYear).Map.Start);
        return TimeTravelResult.Traveled(targetYear, cost);
    }
}
