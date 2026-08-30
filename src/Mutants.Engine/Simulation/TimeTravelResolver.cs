using Mutants.Core.Characters;
using Mutants.Core.Ions;
using Mutants.Core.Levels;
using Mutants.Engine.Combat;

namespace Mutants.Engine.Simulation;

/// <summary>
/// Resolves a time-travel attempt — docs/GDD.md §3.2. Retreating to an
/// already-unlocked, shallower-or-equal level is always free. Reaching a
/// never-before-unlocked level requires the character to meet that
/// level's minimum level and (if the level defines one) defeat its
/// gatekeeper first — win, and the level unlocks; lose, and nothing is
/// charged or changed. Any level gain (new or previously unlocked) then
/// costs <see cref="IonEconomy.TimeTravelCost"/> Ions, per the GDD's
/// exact formula and its "insufficient Ions blocks the jump" failure
/// mode.
/// </summary>
public static class TimeTravelResolver
{
    public static TimeTravelResult Travel(Mutant mutant, GameWorld world, int targetLevel, IRandomSource random)
    {
        var targetDefinition = world.TryGetLevel(targetLevel);
        if (targetDefinition is null)
        {
            return TimeTravelResult.Failed(TimeTravelFailureReason.UnknownLevel);
        }

        var isRetreat = targetLevel <= mutant.CurrentTimeLevel && targetLevel <= mutant.UnlockedTimeLevel;
        if (isRetreat)
        {
            mutant.SetCurrentTimeLevel(targetLevel);
            mutant.PlaceAt(targetDefinition.Map.Start);
            return TimeTravelResult.Traveled(targetLevel);
        }

        FightResult? gatekeeperFight = null;

        if (targetLevel > mutant.UnlockedTimeLevel)
        {
            if (mutant.Level < targetDefinition.MinCharacterLevelToUnlock)
            {
                return TimeTravelResult.Failed(TimeTravelFailureReason.BelowMinimumCharacterLevel);
            }

            if (targetDefinition.Gatekeeper is not null && !mutant.HasDefeatedGatekeeper(targetLevel))
            {
                var gatekeeper = targetDefinition.Gatekeeper();
                gatekeeperFight = CombatResolver.Fight(mutant, gatekeeper, random);
                if (!gatekeeperFight.MutantWon)
                {
                    return TimeTravelResult.Failed(TimeTravelFailureReason.LostToGatekeeper, gatekeeperFight);
                }

                mutant.RecordGatekeeperDefeat(targetLevel);
            }

            mutant.UnlockTimeLevel(targetLevel);
        }

        var cost = IonEconomy.TimeTravelCost(targetLevel);
        if (!mutant.Ions.CanAfford(cost))
        {
            return TimeTravelResult.Failed(TimeTravelFailureReason.InsufficientIons, gatekeeperFight);
        }

        mutant.Ions.Spend(cost);
        mutant.SetCurrentTimeLevel(targetLevel);
        mutant.PlaceAt(targetDefinition.Map.Start);
        return TimeTravelResult.Traveled(targetLevel, gatekeeperFight);
    }
}
