using System.Net;
using System.Net.Sockets;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Time;
using ChronoTravelers.Engine;
using ChronoTravelers.Engine.Content;
using ChronoTravelers.Engine.Npc;
using ChronoTravelers.Game;
using ChronoTravelers.Server;
using ChronoTravelers.Server.Hub;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// ChronoTravelers shared-world server (docs/PLATFORM_STRATEGY.md Option B).
// One TimeWorld ticks on a real clock; players connect over telnet OR the
// SignalR hub (the ChronoTravelers.Console --connect client). Both front
// ends share the one SharedGame.
//
// Usage: ChronoTravelers.Server [--port N] [--http-port N] [--db PATH] [--tick-ms N] [--seed N]

var opts = ParseArgs(args);
var telnetPort = opts.GetInt("--port", EnvInt("CHRONOTRAVELERS_PORT", 4000));
var httpPort = opts.GetInt("--http-port", EnvInt("CHRONOTRAVELERS_HTTP_PORT", 5000));
var tickMs = opts.GetInt("--tick-ms", 2000);
var dbPath = opts.Get("--db") ?? DefaultDbPath();
var seed = opts.GetLong("--seed", DateTime.UtcNow.Ticks);

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);

void Log(string m) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {m}");

Log($"ChronoTravelers server starting — seed {seed}, tick {tickMs}ms, telnet :{telnetPort}, signalr :{httpPort}, db {dbPath}");

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
IReadOnlyDictionary<CharacterClass, double>? npcClassWeights;
try { npcClassWeights = ContentLoader.LoadNpcClassWeights(Path.Combine(contentDir, "npc-population.json")); }
catch (ContentException) { npcClassWeights = null; }
var npcs = NpcPopulation.Spawn(npcCount, world, random, npcClassWeights);
Log($"Spawned {npcs.Count} NPCs across the timeline.");

IReadOnlyList<AbilityData> abilities;
try { abilities = ContentLoader.LoadAbilities(Path.Combine(contentDir, "abilities.json")); }
catch (ContentException) { abilities = []; Log("No ability catalog found — NPCs will grind without abilities."); }

using var store = new ServerStore(dbPath);
var game = new SharedGame(world, npcs, random, npcClassWeights, abilities);

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; Log("Shutdown requested."); shutdown.Cancel(); };

// --- SignalR host --------------------------------------------------------

var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Warning);
builder.WebHost.UseUrls($"http://0.0.0.0:{httpPort}");
builder.Services.AddSignalR();
builder.Services.AddSingleton(game);
builder.Services.AddSingleton(store);
builder.Services.AddSingleton(new WorldSeed(seed));
builder.Services.AddSingleton<HubSessions>();

var app = builder.Build();
app.MapHub<GameHub>("/game");
await app.StartAsync(shutdown.Token).ConfigureAwait(false);
Log($"SignalR hub at http://<host>:{httpPort}/game  —  console: `--connect http://<host>:{httpPort}`");

// --- world tick loop ---------------------------------------------------

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

// --- telnet listener -------------------------------------------------

var listener = new TcpListener(IPAddress.Any, telnetPort);
listener.Start();
Log($"Telnet on :{telnetPort} — `telnet <host> {telnetPort}`.  Ctrl+C to stop.");

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
    await app.StopAsync().ConfigureAwait(false);

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
        : Path.Combine(appData, "ChronoTravelers", "server.db");
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
