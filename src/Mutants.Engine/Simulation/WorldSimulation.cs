using Mutants.Core.Characters;
using Mutants.Core.Events;
using Mutants.Core.Ions;
using Mutants.Core.World;
using Mutants.Engine.Npc;

namespace Mutants.Engine.Simulation;

/// <summary>
/// Orchestrates one "world tick" — docs/GDD.md §9's background tick that
/// advances Ion drain and NPC actions independent of when the human
/// player types a command, and §7's NPC simulation. AGENTS.md assigns
/// "the tick loop, NPC AI" to the Systems/Engine Agent. Real timer-driven
/// ticking (an actual "every ~2 seconds" cadence) is left to whoever
/// hosts this — Mutants.Console currently advances one tick per player
/// command instead, as a synchronous v1 approximation.
/// </summary>
public sealed class WorldSimulation
{
    public LevelMap Level { get; }
    public BroadcastChannel Broadcast { get; }
    public IReadOnlyList<Mutant> Npcs { get; }

    private readonly IRandomSource _random;

    public WorldSimulation(LevelMap level, IReadOnlyList<Mutant> npcs, IRandomSource random, BroadcastChannel? broadcast = null)
    {
        Level = level;
        Npcs = npcs;
        _random = random;
        Broadcast = broadcast ?? new BroadcastChannel();
    }

    /// <summary>
    /// Advances the world by one tick: passive Ion drain for every living
    /// mutant (all NPCs plus <paramref name="player"/>), then one AI
    /// action per living NPC, publishing kill/level-up events to
    /// <see cref="Broadcast"/> along the way.
    /// </summary>
    public void Tick(Mutant player)
    {
        foreach (var mutant in Npcs.Append(player))
        {
            if (mutant.Health.IsDead)
            {
                continue;
            }

            var ticksPerDrain = IonEconomy.TicksPerIonDrain(mutant.UnlockedTimeLevel, mutant.ClassDefinition.IonDrainMultiplier);
            mutant.AdvanceIonDrainTick(ticksPerDrain);
        }

        foreach (var npc in Npcs)
        {
            if (npc.Health.IsDead)
            {
                continue;
            }

            var levelBefore = npc.Level;
            var result = NpcController.Act(npc, Level, _random);

            if (result.Fight is { } fight)
            {
                Broadcast.Publish(fight.MutantWon
                    ? GameEvent.Slain(result.MonsterName!, npc.Name)
                    : GameEvent.Slain(npc.Name, result.MonsterName!));
            }

            if (npc.Level > levelBefore)
            {
                Broadcast.Publish(GameEvent.LevelReached(npc.Name, npc.Level));
            }
        }
    }
}
