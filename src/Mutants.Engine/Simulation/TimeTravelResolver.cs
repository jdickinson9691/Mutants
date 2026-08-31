using Mutants.Core.Characters;
using Mutants.Core.Ions;
using Mutants.Core.Time;

namespace Mutants.Engine.Simulation;

/// <summary>
/// Resolves a <c>travel</c> attempt on the continuous timeline —
/// docs/GDD.md §3.2. Travel is unrestricted: any year in
/// 2000–5000 you can afford. The only checks are that the year is on the
/// timeline and that the Mutant can pay
/// <see cref="IonEconomy.TimeTravelCost"/> (symmetric — retreating costs
/// the same as advancing). No unlock, no minimum character level, no
/// Gatekeeper gate; Gatekeepers are just tough encounters in their year,
/// handled by the fight flow, not here.
/// </summary>
public static class TimeTravelResolver
{
    /// <summary>
    /// <paramref name="random"/> is unused now (kept so callers that
    /// thread an <see cref="IRandomSource"/> don't have to change) — there
    /// is no Gatekeeper fight during travel any more.
    /// </summary>
    public static TimeTravelResult Travel(Mutant mutant, TimeWorld world, int targetYear, IRandomSource random)
    {
        _ = random;

        if (!TimeScale.IsValidYear(targetYear))
        {
            return TimeTravelResult.Failed(TimeTravelFailureReason.YearOutOfRange);
        }

        var cost = IonEconomy.TimeTravelCost(mutant.CurrentYear, targetYear);
        if (cost > 0 && !mutant.Ions.CanAfford(cost))
        {
            return TimeTravelResult.Failed(TimeTravelFailureReason.InsufficientIons);
        }

        if (cost > 0)
        {
            mutant.Ions.Spend(cost);
        }

        mutant.SetCurrentYear(targetYear);
        mutant.PlaceAt(world.GetYear(targetYear).Map.Start);
        return TimeTravelResult.Traveled(targetYear, cost);
    }
}
