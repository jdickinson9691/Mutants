using ChronoTravelers.Core.Classes;
using ChronoTravelers.Engine.Persistence;
using ChronoTravelers.Game;
using Microsoft.AspNetCore.SignalR;

namespace ChronoTravelers.Server.Hub;

/// <summary>Holds the server's world seed for DI (used when saving characters).</summary>
public sealed record WorldSeed(long Value);

/// <summary>
/// The SignalR front end onto the same <see cref="SharedGame"/> the telnet
/// listener uses. String-based wire protocol — no shared DTO assembly:
/// <c>Login</c> / <c>Characters</c> / <c>OfferedClasses</c> / <c>Continue</c> /
/// <c>CreateCharacter</c> for the handshake, then <c>Send</c> for commands;
/// all game output (command results, tick narration, the feed,
/// announcements) is pushed to the client's <c>Receive</c> method.
/// </summary>
public sealed class GameHub : Microsoft.AspNetCore.SignalR.Hub
{
    private readonly SharedGame _game;
    private readonly ServerStore _store;
    private readonly WorldSeed _seed;
    private readonly HubSessions _sessions;
    private readonly IHubContext<GameHub> _hubContext;

    public GameHub(SharedGame game, ServerStore store, WorldSeed seed, HubSessions sessions, IHubContext<GameHub> hubContext)
    {
        _game = game;
        _store = store;
        _seed = seed;
        _sessions = sessions;
        _hubContext = hubContext;
    }

    private HubConn Conn => _sessions.Get(Context.ConnectionId)
        ?? throw new HubException("Connection not initialised.");

    private static void Log(string m) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {m}");

    public override Task OnConnectedAsync()
    {
        _sessions.Open(Context.ConnectionId, _hubContext);
        return base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (_sessions.Remove(Context.ConnectionId, out var conn) && conn is not null)
        {
            if (conn.Session is not null && conn.Account is not null)
            {
                try
                {
                    _store.SaveCharacter(conn.Account, conn.Session.Player, _seed.Value);
                    _game.Leave(conn.Session);
                    Log($"signalr {conn.Account}/{conn.Session.Player.Name} left ({_game.OnlineCount} online)");
                }
                catch { /* best effort */ }
            }

            await conn.DisposeAsync();
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <returns>"ok" | "created" | "badpassword" | "invalid: &lt;why&gt;"</returns>
    public string Login(string account, string password)
    {
        account = (account ?? "").Trim();
        if (account.Length is < 3 or > 20 || !account.All(c => char.IsLetterOrDigit(c) || c is '_' or '-'))
        {
            return "invalid: names are 3-20 chars, letters/digits/_/- only";
        }

        if (string.IsNullOrEmpty(password) || password.Length < 6)
        {
            return "invalid: password must be at least 6 characters";
        }

        var existing = _store.FindAccount(account);
        if (existing is not null)
        {
            if (!PasswordHash.Verify(password, existing.Salt, existing.Hash))
            {
                return "badpassword";
            }

            Conn.Account = existing.DisplayName;
            return "ok";
        }

        var created = _store.CreateAccount(account, password);
        Conn.Account = created.DisplayName;
        return "created";
    }

    private string RequireAccount() => Conn.Account ?? throw new HubException("Log in first.");

    /// <returns>Newline-joined "N. Name the Class — level L, Y A.D." lines; empty if none.</returns>
    public string Characters()
    {
        var account = RequireAccount();
        var saved = _store.CharactersFor(account);
        return string.Join('\n', saved.Select((c, i) =>
            $"{i + 1}. {c.Name} the {c.Class} — level {c.Level}, furthest {c.FurthestYearReached} A.D."));
    }

    /// <returns>Comma-separated class names this account may pick for a new Traveler.</returns>
    public string OfferedClasses()
    {
        var account = RequireAccount();
        return string.Join(',', CharacterFactory.OfferedClasses(_store.CharactersFor(account)).Select(c => c.ToString()));
    }

    /// <returns>"joined" | "error: &lt;why&gt;"</returns>
    public string Continue(int index)
    {
        var account = RequireAccount();
        if (Conn.Session is not null)
        {
            return "error: already playing";
        }

        var saved = _store.CharactersFor(account);
        if (index < 1 || index > saved.Count)
        {
            return "error: no such Traveler";
        }

        var data = _store.LoadCharacter(account, saved[index - 1].Name)!;
        Conn.Session = _game.Join(account, CharacterMapper.FromSaveData(data), Conn);
        Log($"signalr {account}/{data.Name} joined ({_game.OnlineCount} online)");
        return "joined";
    }

    /// <returns>"joined" | "error: &lt;why&gt;"</returns>
    public string CreateCharacter(string name, string className)
    {
        var account = RequireAccount();
        if (Conn.Session is not null)
        {
            return "error: already playing";
        }

        name = (name ?? "").Trim();
        if (name.Length is < 2 or > 20)
        {
            return "error: name must be 2-20 characters";
        }

        var saved = _store.CharactersFor(account);
        if (saved.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return "error: you already have a Traveler by that name";
        }

        if (!Enum.TryParse<CharacterClass>(className, ignoreCase: true, out var cc)
            || !CharacterFactory.OfferedClasses(saved).Contains(cc))
        {
            return "error: that role isn't available";
        }

        Conn.Session = _game.Join(account, CharacterFactory.NewTraveler(name, cc), Conn);
        Log($"signalr {account}/{name} joined ({_game.OnlineCount} online)");
        return "joined";
    }

    public void Send(string line)
    {
        var conn = Conn;
        if (conn.Session is null)
        {
            conn.Line("Pick a Traveler first.");
            return;
        }

        _game.Execute(conn.Session, line ?? "");
    }
}
