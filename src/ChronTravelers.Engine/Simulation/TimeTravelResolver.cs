using ChronTravelers.Core.Characters;
using ChronTravelers.Core.Ions;
using ChronTravelers.Core.Time;

namespace ChronTravelers.Engine.Simulation;

/// <summary>
/// Resolves a <c>travel</c> attempt on the continuous timeline —
/// docs/GDD.md §3.2. Travel is unrestricted: any year in
/// 2000–5000 you can afford. The only checks are that the year is on the
/// timeline and that the Traveler can pay
/// <see cref="IonEconomy.TimeTravelCost"/> (symmetric — retreating costs
/// the same as advancing). No unlock, no minimum character level, no
/// Warden gate; Wardens are just tough encounters in their year,
/// handled by the fight flow, not here.
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

        var cost = IonEconomy.TimeTravelCost(traveler.CurrentYear, targetYear);
        if (cost > 0 && !traveler.Ions.CanAfford(cost))
        {
            return TimeTravelResult.Failed(TimeTravelFailureReason.InsufficientIons);
        }

        if (cost > 0)
        {
            traveler.Ions.Spend(cost);
        }

        traveler.SetCurrentYear(targetYear);
        traveler.PlaceAt(world.GetYear(targetYear).Map.Start);
        return TimeTravelResult.Traveled(targetYear, cost);
    }
}
