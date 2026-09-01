using System.Net;
using System.Net.Sockets;
using ChronTravelers.Core.Time;
using ChronTravelers.Engine;
using ChronTravelers.Engine.Content;
using ChronTravelers.Engine.Npc;
using ChronTravelers.Game;
using ChronTravelers.Server;

// ChronTravelers shared-world server (docs/PLATFORM_STRATEGY.md Option B).
// One TimeWorld ticks on a real clock; telnet clients log into an account,
// pick or make a Traveler, and play alongside each other and the NPCs.
//
// Usage: ChronTravelers.Server [--port N] [--db PATH] [--tick-ms N] [--seed N]

var opts = ParseArgs(args);
var port = opts.GetInt("--port", EnvInt("CHRONTRAVELERS_PORT", 4000));
var tickMs = opts.GetInt("--tick-ms", 2000);
var dbPath = opts.Get("--db") ?? DefaultDbPath();
var seed = opts.GetLong("--seed", DateTime.UtcNow.Ticks);

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);

void Log(string m) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {m}");

Log($"ChronTravelers server starting — seed {seed}, tick {tickMs}ms, db {dbPath}");

var contentDir = Path.Combine(AppContext.BaseDirectory, "Content");
TimeWorld world;
try
{
    world = ContentLoader.LoadTimeWorld(contentDir, seed);
    Log("Loaded the shipped timeline content.");
}
catch (ContentException ex)
{
    Log($"Content load failed ({ex.Message}); using the built-in sandbox timeline.");
    world = TestTimeWorld.Build(seed);
}

var random = new SystemRandomSource();
int npcCount;
try { npcCount = ContentLoader.LoadNpcCount(Path.Combine(contentDir, "npc-population.json")); }
catch (ContentException) { npcCount = 20; }
var npcs = NpcPopulation.Spawn(npcCount, world, random);
Log($"Spawned {npcs.Count} NPCs across the timeline.");

using var store = new ServerStore(dbPath);
var game = new SharedGame(world, npcs, random);

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; Log("Shutdown requested."); shutdown.Cancel(); };

// World tick loop.
var tickLoop = Task.Run(async () =>
{
    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(tickMs));
    var sinceSave = 0;
    while (await timer.WaitForNextTickAsync(shutdown.Token).ConfigureAwait(false))
    {
        try
        {
            game.Tick();
            if (++sinceSave >= Math.Max(1, 60_000 / tickMs))
            {
                sinceSave = 0;
                foreach (var s in game.SnapshotSessions())
                {
                    try { store.SaveCharacter(s.AccountName, s.Player, seed); } catch { /* keep ticking */ }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"tick error: {ex}");
        }
    }
});

var listener = new TcpListener(IPAddress.Any, port);
listener.Start();
Log($"Listening on port {port}. `telnet <host> {port}` to connect.  Ctrl+C to stop.");

try
{
    while (!shutdown.IsCancellationRequested)
    {
        TcpClient client;
        try
        {
            client = await listener.AcceptTcpClientAsync(shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            break;
        }

        _ = Task.Run(() => new TelnetConnection(client, game, store, seed, Log).Run());
    }
}
finally
{
    listener.Stop();
    try { await tickLoop.ConfigureAwait(false); } catch (OperationCanceledException) { }

    foreach (var s in game.SnapshotSessions())
    {
        try { store.SaveCharacter(s.AccountName, s.Player, seed); } catch { /* best effort */ }
    }

    Log("Stopped.");
}

static string DefaultDbPath()
{
    var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    return string.IsNullOrEmpty(appData)
        ? Path.Combine(AppContext.BaseDirectory, "server.db")
        : Path.Combine(appData, "ChronTravelers", "server.db");
}

static int EnvInt(string name, int fallback) =>
    int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : fallback;

static Args ParseArgs(string[] argv)
{
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < argv.Length - 1; i++)
    {
        if (argv[i].StartsWith("--"))
        {
            map[argv[i]] = argv[i + 1];
            i++;
        }
    }

    return new Args(map);
}

sealed class Args(Dictionary<string, string> map)
{
    public string? Get(string k) => map.TryGetValue(k, out var v) ? v : null;
    public int GetInt(string k, int fallback) => int.TryParse(Get(k), out var v) ? v : fallback;
    public long GetLong(string k, long fallback) => long.TryParse(Get(k), out var v) ? v : fallback;
}
