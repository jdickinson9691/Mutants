using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Events;
using ChronoTravelers.Core.Time;
using ChronoTravelers.Engine;
using ChronoTravelers.Engine.Simulation;

namespace ChronoTravelers.Game;

/// <summary>
/// One shared, ticking timeline that any number of players connect into —
/// the server-side counterpart to the console's single-player loop. Owns a
/// <see cref="TimeWorld"/>, the NPC population, and a
/// <see cref="WorldSimulation"/>; NPCs keep the world populated when few
/// humans are online. Every mutation (a command, the world tick, a
/// join/leave) runs under one lock, so the tick and a command never race.
/// </summary>
public sealed class SharedGame
{
    private readonly object _gate = new();
    private readonly WorldSimulation _sim;
    private readonly List<Traveler> _npcs;
    private readonly List<Session> _sessions = [];

    /// <param name="npcClassWeights">
    /// Optional per-class spawn weights (docs/CONTENT_PLAN.md's "config-driven
    /// NPC class distribution") passed straight through to the
    /// <see cref="WorldSimulation"/> this game owns, so a respawned NPC keeps
    /// drawing from the same distribution the initial <paramref name="npcs"/>
    /// population did. Null (the default) means uniform-random, unchanged.
    /// </param>
    public SharedGame(TimeWorld world, IEnumerable<Traveler> npcs, IRandomSource random, IReadOnlyDictionary<CharacterClass, double>? npcClassWeights = null)
    {
        World = world;
        _npcs = npcs.ToList();
        _sim = new WorldSimulation(world, _npcs, random, npcClassWeights: npcClassWeights);
    }

    public TimeWorld World { get; }
    public BroadcastChannel Broadcast => _sim.Broadcast;

    public int OnlineCount
    {
        get { lock (_gate) { return _sessions.Count; } }
    }

    public IReadOnlyList<Session> SnapshotSessions()
    {
        lock (_gate) { return _sessions.ToList(); }
    }

    internal IReadOnlyList<Traveler> Npcs => _npcs;

    /// <summary>Adds a player to the world (placed at its year's start room if it isn't on the map), announces the arrival, and returns the session.</summary>
    public Session Join(string account, Traveler player, IGameOutput output)
    {
        lock (_gate)
        {
            var map = World.GetYear(player.CurrentYear).Map;
            if (map.TryGetRoom(player.Position) is null)
            {
                player.PlaceAt(map.Start);
            }

            var session = new Session(account, player, output)
            {
                ShownBroadcast = Broadcast.Events.Count, // start fresh — no backlog dump
            };
            _sessions.Add(session);

            AnnounceExcept(session.Id, $"{player.Name} the {player.Class} has surfaced in {player.CurrentYear} A.D.");
            session.Send($"You're on the shared timeline with {_sessions.Count - 1} other Traveler(s) and {_npcs.Count} NPC(s).");
            Render.Room(this, session);
            return session;
        }
    }

    public void Leave(Session session)
    {
        lock (_gate)
        {
            if (_sessions.Remove(session))
            {
                AnnounceExcept(session.Id, $"{session.Player.Name} has faded from the timeline.");
            }
        }
    }

    /// <summary>Runs one command line for a session and flushes any feed lines it produced.</summary>
    public void Execute(Session session, string line)
    {
        lock (_gate)
        {
            Commands.Run(this, session, line ?? "");
            FlushFeed(session);
        }
    }

    /// <summary>Advances the whole world one tick and pushes each player their narration + new feed lines. A no-op while nobody is connected.</summary>
    public void Tick()
    {
        lock (_gate)
        {
            if (_sessions.Count == 0)
            {
                return;
            }

            _sim.TickMultiplayer(_sessions.Select(s => s.TickState).ToList());

            foreach (var session in _sessions)
            {
                foreach (var narrationLine in session.TickState.Narration)
                {
                    session.Send(narrationLine);
                }

                FlushFeed(session);

                if (session.Player.Health.IsDead)
                {
                    Respawn(session);
                }
            }
        }
    }

    internal void AnnounceExcept(Guid exceptId, string text)
    {
        foreach (var session in _sessions)
        {
            if (session.Id != exceptId)
            {
                session.Send($"[world] {text}");
            }
        }
    }

    internal void AnnounceAll(string text)
    {
        foreach (var session in _sessions)
        {
            session.Send($"[world] {text}");
        }
    }

    /// <summary>Other players standing in the same room as <paramref name="session"/>.</summary>
    internal IEnumerable<Session> PlayersWith(Session session) =>
        _sessions.Where(s => s.Id != session.Id
            && s.Player.CurrentYear == session.Player.CurrentYear
            && s.Player.Position.Equals(session.Player.Position));

    internal IReadOnlyList<Session> AllSessions() => _sessions;

    private void FlushFeed(Session session)
    {
        var events = Broadcast.Events;
        for (var i = session.ShownBroadcast; i < events.Count; i++)
        {
            var e = events[i];
            var inHisYear = e.Year is null || e.Year == session.Player.CurrentYear;
            if (inHisYear || e.Kind == GameEventKind.Ambushed)
            {
                session.Send($"* {e.Message}");
            }
        }

        session.ShownBroadcast = events.Count;
    }

    private void Respawn(Session session)
    {
        var player = session.Player;
        player.SetCurrentYear(TimeScale.MinYear);
        var start = World.GetYear(TimeScale.MinYear).Map.Start;
        player.PlaceAt(start);
        player.Health.Heal(player.Health.Max);
        session.Send("You were struck down — the surge carries what's left of you back upstream to 2000 A.D. You come to at full health.");
        AnnounceExcept(session.Id, $"{player.Name} was pulled back upstream after falling.");
        Render.Room(this, session);
    }
}
