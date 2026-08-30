using Mutants.Core.Characters;
using Mutants.Core.Events;
using Mutants.Core.Ions;
using Mutants.Core.Levels;
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
///
/// Each NPC acts against its OWN <see cref="Mutant.CurrentTimeLevel"/> —
/// the map, monster roster, and store list are all resolved per-NPC from
/// <see cref="World"/> every tick, rather than one shared level for
/// everyone, since NPCs are free to time-travel independently of the
/// player (see <see cref="Npc.NpcController"/>'s travel behavior).
/// </summary>
public sealed class WorldSimulation
{
    public GameWorld World { get; }
    public BroadcastChannel Broadcast { get; }
    public IReadOnlyList<Mutant> Npcs { get; }

    private readonly IRandomSource _random;

    public WorldSimulation(
        GameWorld world,
        IReadOnlyList<Mutant> npcs,
        IRandomSource random,
        BroadcastChannel? broadcast = null)
    {
        World = world;
        Npcs = npcs;
        _random = random;
        Broadcast = broadcast ?? new BroadcastChannel();
    }

    /// <summary>
    /// Advances the world by one tick: passive Ion drain for every living
    /// mutant (all NPCs plus <paramref name="player"/>), then one AI
    /// action per living NPC on its own current level (which may trade,
    /// travel, or fight — see <see cref="Npc.NpcController"/>), publishing
    /// kill/level-up/time-travel events to <see cref="Broadcast"/> along
    /// the way.
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

            var levelDefinition = World.TryGetLevel(npc.CurrentTimeLevel);
            if (levelDefinition is null)
            {
                continue; // shouldn't happen outside a corrupt save, but don't crash the tick over it
            }

            var activeStores = levelDefinition.StoreSlots
                .Where(slot => slot.Store is not null)
                .Select(slot => slot.Store!)
                .ToList();

            var levelBefore = npc.Level;
            var timeLevelBefore = npc.CurrentTimeLevel;
            var result = NpcController.Act(npc, levelDefinition.Map, _random, activeStores, levelDefinition.MonsterRoster, World);

            if (result.Fight is { } fight)
            {
                Broadcast.Publish(fight.MutantWon
                    ? GameEvent.Slain(result.MonsterName!, npc.Name)
                    : GameEvent.Slain(npc.Name, result.MonsterName!));
            }

            if (npc.CurrentTimeLevel != timeLevelBefore)
            {
                Broadcast.Publish(GameEvent.TimeTraveled(npc.Name, npc.CurrentTimeLevel));
            }

            if (npc.Level > levelBefore)
            {
                Broadcast.Publish(GameEvent.LevelReached(npc.Name, npc.Level));
            }
        }
    }
}
