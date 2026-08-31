using Mutants.Core.Characters;
using Mutants.Core.Events;
using Mutants.Core.Ions;
using Mutants.Core.Time;
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
    public TimeWorld World { get; }
    public BroadcastChannel Broadcast { get; }
    public IReadOnlyList<Mutant> Npcs { get; }

    private readonly IRandomSource _random;

    // Where the player was at the end of the previous tick — lets the
    // monster sim tell "stood still" (ambushable) from "just arrived".
    private int? _lastPlayerYear;
    private Mutants.Core.World.Coordinate _lastPlayerPosition;

    public WorldSimulation(
        TimeWorld world,
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
    /// Advances the world by one tick: passive Ion drain and potion-buff
    /// expiry for every living mutant (all NPCs plus
    /// <paramref name="player"/>), then one AI action per living NPC on
    /// its own current level (which may trade, travel, or fight — see
    /// <see cref="Npc.NpcController"/>), publishing kill/level-up/
    /// time-travel events to <see cref="Broadcast"/> along the way.
    /// </summary>
    /// <param name="playerActedIdly">
    /// True if the turn the player just took was an informational no-op
    /// (look / status / inventory / …) rather than a real action. Only then
    /// — and only if they also held position — can a co-located monster
    /// ambush them. Defaults to false so non-console callers never ambush.
    /// </param>
    public void Tick(Mutant player, bool playerActedIdly = false)
    {
        foreach (var mutant in Npcs.Append(player))
        {
            if (mutant.Health.IsDead)
            {
                continue;
            }

            var scalingTier = TimelineContentFactory.DisplayTier(mutant.CurrentYear);
            var drainMultiplier = mutant.ClassDefinition.IonDrainMultiplier;
            mutant.AdvanceIonDrainTick(IonEconomy.TicksPerIonDrain(scalingTier, drainMultiplier));
            mutant.AdvanceIonRegenTick(IonEconomy.TicksPerIonRegen(scalingTier, drainMultiplier));
            mutant.AdvanceEffectTicks();
        }

        foreach (var npc in Npcs)
        {
            if (npc.Health.IsDead)
            {
                continue;
            }

            if (!TimeScale.IsValidYear(npc.CurrentYear))
            {
                continue; // shouldn't happen (SetCurrentYear clamps), but don't crash the tick over it
            }

            var yearContent = World.GetYear(npc.CurrentYear);

            var activeStores = yearContent.StoreSlots
                .Where(slot => slot.Store is not null)
                .Select(slot => slot.Store!)
                .ToList();

            var levelBefore = npc.Level;
            var yearBefore = npc.CurrentYear;
            var result = NpcController.Act(npc, yearContent.Map, _random, activeStores, yearContent.MonsterRoster, World);

            if (result.Fight is { } fight)
            {
                Broadcast.Publish(fight.MutantWon
                    ? GameEvent.Slain(result.MonsterName!, npc.Name)
                    : GameEvent.Slain(npc.Name, result.MonsterName!));
            }

            if (npc.CurrentYear != yearBefore)
            {
                Broadcast.Publish(GameEvent.TimeTraveled(npc.Name, npc.CurrentYear));
            }

            if (npc.Level > levelBefore)
            {
                Broadcast.Publish(GameEvent.LevelReached(npc.Name, npc.Level));
            }
        }

        // Only the year the player is standing in runs live spatial monster
        // simulation (movement, infighting, healing); other years' monsters
        // stay frozen where they were placed until visited.
        if (Mutants.Core.Time.TimeScale.IsValidYear(player.CurrentYear))
        {
            var lingered = playerActedIdly
                && _lastPlayerYear == player.CurrentYear
                && _lastPlayerPosition.Equals(player.Position);
            var previousPosition = _lastPlayerYear == player.CurrentYear ? _lastPlayerPosition : player.Position;
            var here = World.GetYear(player.CurrentYear);
            var safeRooms = here.StoreSlots.Select(slot => slot.Location).ToHashSet();
            MonsterController.Tick(here.Population, here.Map, here.MonsterRoster, player, previousPosition, lingered, _random, Broadcast, safeRooms);
        }

        _lastPlayerYear = player.CurrentYear;
        _lastPlayerPosition = player.Position;
    }
}
