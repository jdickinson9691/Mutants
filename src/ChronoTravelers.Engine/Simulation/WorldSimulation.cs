using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Events;
using ChronoTravelers.Core.Tachyons;
using ChronoTravelers.Core.Time;
using ChronoTravelers.Engine.Npc;

namespace ChronoTravelers.Engine.Simulation;

/// <summary>
/// Orchestrates one "world tick" — docs/GDD.md §9's background tick that
/// advances Tachyon drain and NPC actions independent of when the human
/// player types a command, and §7's NPC simulation. AGENTS.md assigns
/// "the tick loop, NPC AI" to the Systems/Engine Agent. Real timer-driven
/// ticking (an actual "every ~2 seconds" cadence) is left to whoever
/// hosts this — ChronoTravelers.Console currently advances one tick per player
/// command instead, as a synchronous v1 approximation.
///
/// Each NPC acts against its OWN <see cref="Traveler.CurrentTimeLevel"/> —
/// the map, monster roster, and store list are all resolved per-NPC from
/// <see cref="World"/> every tick, rather than one shared level for
/// everyone, since NPCs are free to time-travel independently of the
/// player (see <see cref="Npc.NpcController"/>'s travel behavior).
/// </summary>
public sealed class WorldSimulation
{
    public TimeWorld World { get; }
    public BroadcastChannel Broadcast { get; }

    /// <summary>
    /// The live NPC population, stored (not copied) from the constructor's
    /// <paramref name="npcs"/> argument, so a dead local-pool NPC can be
    /// replaced in place — see <see cref="RespawnDeadNpcs"/> — and the
    /// caller's own list reference (ChronoTravelers.Console's <c>npcs</c>
    /// local, <see cref="ChronoTravelers.Game.SharedGame"/>'s <c>_npcs</c>
    /// field) sees the replacement without either side re-syncing anything.
    /// </summary>
    public IList<Traveler> Npcs { get; }

    /// <summary>
    /// Player-local ambient narration from the most recent <see cref="Tick"/>
    /// — "you hear something to the north," "the Alley Runner slips away
    /// east," etc. Rebuilt each tick; not part of the shared kill-feed.
    /// </summary>
    public IReadOnlyList<string> LastTickNarration => _narration;

    private readonly IRandomSource _random;
    private readonly List<string> _narration = [];

    // Where the player was at the end of the previous tick — lets the
    // monster sim tell "stood still" (ambushable) from "just arrived".
    private int? _lastPlayerYear;
    private ChronoTravelers.Core.World.Coordinate _lastPlayerPosition;

    public WorldSimulation(
        TimeWorld world,
        IList<Traveler> npcs,
        IRandomSource random,
        BroadcastChannel? broadcast = null)
    {
        World = world;
        Npcs = npcs;
        _random = random;
        Broadcast = broadcast ?? new BroadcastChannel();
    }

    /// <summary>
    /// Replaces any dead entry in <see cref="Npcs"/> in place — a local-pool
    /// slot (index below <see cref="NpcPopulation.LocalPopulationTarget"/>)
    /// comes back near <paramref name="anchorYear"/> via
    /// <see cref="NpcPopulation.RespawnNear"/> so it's still somewhere the
    /// player can run into it; any other slot (or a local slot when there's
    /// no anchor yet) comes back from a whole-timeline draw via
    /// <see cref="NpcPopulation.RespawnScattered"/>, same as the original
    /// no-respawn-locality design.
    /// </summary>
    private void RespawnDeadNpcs(int? anchorYear)
    {
        for (var i = 0; i < Npcs.Count; i++)
        {
            if (!Npcs[i].Health.IsDead)
            {
                continue;
            }

            Npcs[i] = i < NpcPopulation.LocalPopulationTarget && anchorYear is { } anchor
                ? NpcPopulation.RespawnNear(i, anchor, World, _random)
                : NpcPopulation.RespawnScattered(i, World, _random);
        }
    }

    /// <summary>
    /// Advances the world by one tick: passive Tachyon drain and potion-buff
    /// expiry for every living traveler (all NPCs plus
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
    public void Tick(Traveler player, bool playerActedIdly = false)
    {
        _narration.Clear();

        foreach (var traveler in Npcs.Append(player))
        {
            if (traveler.Health.IsDead)
            {
                continue;
            }

            var scalingTier = TimelineContentFactory.DisplayTier(traveler.CurrentYear);
            var drainMultiplier = traveler.ClassDefinition.TachyonDrainMultiplier;
            traveler.AdvanceTachyonDrainTick(TachyonEconomy.TicksPerTachyonDrain(scalingTier, drainMultiplier));
            traveler.AdvanceTachyonRegenTick(TachyonEconomy.TicksPerTachyonRegen(scalingTier, drainMultiplier));
            traveler.AdvanceEffectTicks();
        }

        var playerAnchorYear = TimeScale.IsValidYear(player.CurrentYear) ? player.CurrentYear : (int?)null;
        RespawnDeadNpcs(playerAnchorYear);

        for (var i = 0; i < Npcs.Count; i++)
        {
            var npc = Npcs[i];

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
            var pullToAnchor = i < NpcPopulation.LocalPopulationTarget;
            var result = NpcController.Act(npc, yearContent.Map, _random, activeStores, yearContent.MonsterRoster, World, playerAnchorYear, pullToAnchor);

            if (result.Fight is { } fight)
            {
                Broadcast.Publish(fight.TravelerWon
                    ? GameEvent.Slain(result.MonsterName!, npc.Name, yearBefore, victimIsCreature: true)
                    : GameEvent.Slain(npc.Name, result.MonsterName!, yearBefore, killerIsCreature: true));
            }

            if (npc.CurrentYear != yearBefore)
            {
                Broadcast.Publish(GameEvent.TimeTraveled(npc.Name, npc.CurrentYear));
            }

            if (npc.Level > levelBefore)
            {
                Broadcast.Publish(GameEvent.LevelReached(npc.Name, npc.Level, npc.CurrentYear));
            }
        }

        // The year the player is standing in runs the full spatial monster
        // sim — movement plus aggro / shadowing / ambush / narration.
        if (ChronoTravelers.Core.Time.TimeScale.IsValidYear(player.CurrentYear))
        {
            var lingered = playerActedIdly
                && _lastPlayerYear == player.CurrentYear
                && _lastPlayerPosition.Equals(player.Position);
            var previousPosition = _lastPlayerYear == player.CurrentYear ? _lastPlayerPosition : player.Position;
            var here = World.GetYear(player.CurrentYear);
            var safeRooms = here.StoreSlots.Select(slot => slot.Location).ToHashSet();
            MonsterController.Tick(here.Population, here.Map, here.MonsterRoster, player.CurrentYear, player, previousPosition, lingered, _random, Broadcast, safeRooms, _narration);
        }

        // Every other year that's been instantiated this session (the
        // player's past stops, and every year an NPC is currently in) runs
        // an unattended sim so its monsters keep fighting each other,
        // dropping loot, healing and respawning while the player is away —
        // docs/GDD.md §7.1. Bounded by the memo cache, which only holds
        // years someone has actually entered.
        foreach (var year in World.VisitedYears.ToList())
        {
            if (year == player.CurrentYear)
            {
                continue;
            }

            var content = World.GetYear(year);
            var safe = content.StoreSlots.Select(slot => slot.Location).ToHashSet();
            MonsterController.TickUnattended(content.Population, content.Map, content.MonsterRoster, year, _random, Broadcast, safe);
        }

        _lastPlayerYear = player.CurrentYear;
        _lastPlayerPosition = player.Position;
    }

    private long _mpTick;

    /// <summary>
    /// The multi-player counterpart to <see cref="Tick"/> for a shared-world
    /// host (ChronoTravelers.Game / ChronoTravelers.Server): advances Tachyon drain /
    /// regen / buff-expiry once for every connected player <em>and</em> every
    /// NPC, runs one NPC AI pass, then the spatial monster sim — once per
    /// distinct year that has a player in it (anchored on a rotating player
    /// each tick, so ambush exposure and ambient narration are shared out
    /// fairly among anyone co-located), and an unattended pass for every
    /// other instantiated year. Each <see cref="PlayerTickState"/>'s
    /// <see cref="PlayerTickState.Narration"/> is cleared and refilled; the
    /// caller drains it after the call.
    /// </summary>
    public void TickMultiplayer(IReadOnlyList<PlayerTickState> players)
    {
        foreach (var ps in players)
        {
            ps.Narration.Clear();
        }

        foreach (var traveler in Npcs.Concat(players.Select(p => p.Player)))
        {
            if (traveler.Health.IsDead)
            {
                continue;
            }

            var scalingTier = TimelineContentFactory.DisplayTier(traveler.CurrentYear);
            var drainMultiplier = traveler.ClassDefinition.TachyonDrainMultiplier;
            traveler.AdvanceTachyonDrainTick(TachyonEconomy.TicksPerTachyonDrain(scalingTier, drainMultiplier));
            traveler.AdvanceTachyonRegenTick(TachyonEconomy.TicksPerTachyonRegen(scalingTier, drainMultiplier));
            traveler.AdvanceEffectTicks();
        }

        // A rotating occupied-year anchor, computed before the NPC pass so
        // the local pool (see NpcPopulation.LocalPopulationTarget) has
        // somewhere live to gravitate toward and respawn near this tick —
        // same idea as Tick's player.CurrentYear anchor, but shared-world
        // NPCs have no single player to follow, so the anchor rotates
        // through whichever years actually have someone in them.
        var candidateYears = players
            .Where(p => !p.Player.Health.IsDead && TimeScale.IsValidYear(p.Player.CurrentYear))
            .Select(p => p.Player.CurrentYear)
            .Distinct()
            .OrderBy(y => y)
            .ToList();
        var mpAnchorYear = candidateYears.Count > 0
            ? candidateYears[(int)(_mpTick % candidateYears.Count)]
            : (int?)null;

        RespawnDeadNpcs(mpAnchorYear);

        for (var i = 0; i < Npcs.Count; i++)
        {
            var npc = Npcs[i];

            if (npc.Health.IsDead || !TimeScale.IsValidYear(npc.CurrentYear))
            {
                continue;
            }

            var yearContent = World.GetYear(npc.CurrentYear);
            var activeStores = yearContent.StoreSlots
                .Where(slot => slot.Store is not null)
                .Select(slot => slot.Store!)
                .ToList();

            var levelBefore = npc.Level;
            var yearBefore = npc.CurrentYear;
            var pullToAnchor = i < NpcPopulation.LocalPopulationTarget;
            var result = NpcController.Act(npc, yearContent.Map, _random, activeStores, yearContent.MonsterRoster, World, mpAnchorYear, pullToAnchor);

            if (result.Fight is { } fight)
            {
                Broadcast.Publish(fight.TravelerWon
                    ? GameEvent.Slain(result.MonsterName!, npc.Name, yearBefore, victimIsCreature: true)
                    : GameEvent.Slain(npc.Name, result.MonsterName!, yearBefore, killerIsCreature: true));
            }

            if (npc.CurrentYear != yearBefore)
            {
                Broadcast.Publish(GameEvent.TimeTraveled(npc.Name, npc.CurrentYear));
            }

            if (npc.Level > levelBefore)
            {
                Broadcast.Publish(GameEvent.LevelReached(npc.Name, npc.Level, npc.CurrentYear));
            }
        }

        var attendedYears = new HashSet<int>();
        foreach (var grp in players
            .Where(p => !p.Player.Health.IsDead && TimeScale.IsValidYear(p.Player.CurrentYear))
            .GroupBy(p => p.Player.CurrentYear))
        {
            var year = grp.Key;
            attendedYears.Add(year);

            var content = World.GetYear(year);
            var safe = content.StoreSlots.Select(slot => slot.Location).ToHashSet();

            var anchors = grp.OrderBy(p => p.Player.Name, StringComparer.Ordinal).ToList();
            var anchor = anchors[(int)(_mpTick % anchors.Count)];

            var lingered = anchor.ActedIdly
                && anchor.LastYear == year
                && anchor.LastPosition.Equals(anchor.Player.Position);
            var previous = anchor.LastYear == year ? anchor.LastPosition : anchor.Player.Position;

            MonsterController.Tick(
                content.Population, content.Map, content.MonsterRoster, year,
                anchor.Player, previous, lingered, _random, Broadcast, safe, anchor.Narration);
        }

        foreach (var year in World.VisitedYears.ToList())
        {
            if (attendedYears.Contains(year))
            {
                continue;
            }

            var content = World.GetYear(year);
            var safe = content.StoreSlots.Select(slot => slot.Location).ToHashSet();
            MonsterController.TickUnattended(content.Population, content.Map, content.MonsterRoster, year, _random, Broadcast, safe);
        }

        foreach (var ps in players)
        {
            ps.LastYear = ps.Player.CurrentYear;
            ps.LastPosition = ps.Player.Position;
            ps.ActedIdly = false;
        }

        _mpTick++;
    }
}

/// <summary>Per-connected-player state a shared-world host threads through <see cref="WorldSimulation.TickMultiplayer"/>.</summary>
public sealed class PlayerTickState
{
    public required Traveler Player { get; init; }

    /// <summary>Set by the host before a tick if the player's last command was an informational no-op (drives ambush eligibility, same as the single-player path).</summary>
    public bool ActedIdly { get; set; }

    internal int? LastYear { get; set; }
    internal ChronoTravelers.Core.World.Coordinate LastPosition { get; set; }

    /// <summary>Player-local ambient lines from the most recent tick this player was the anchor for. Cleared and refilled by TickMultiplayer.</summary>
    public List<string> Narration { get; } = [];
}
