using System.Net;
using System.Net.Sockets;
using System.Text;
using ChronoTravelers.Core.Time;
using ChronoTravelers.Engine;
using ChronoTravelers.Game;

namespace ChronoTravelers.Server.Tests;

/// <summary>
/// End-to-end integration coverage for TelnetConnection.cs over a real
/// loopback socket — the login prompt sequence, character creation, a
/// command, and quit, exactly as a real telnet client would experience
/// them. `internal` — reachable here only via the InternalsVisibleTo in
/// AssemblyInfo.cs.
///
/// Each test spins up its own TcpListener on an OS-assigned port, its own
/// throwaway ServerStore, and runs TelnetConnection.Run() on a background
/// thread (it's a synchronous blocking loop) while the test drives it as
/// the client end of the socket.
/// </summary>
public class TelnetConnectionTests : IDisposable
{
    // Generous on purpose: these are full socket round-trips against a real
    // blocking session loop. 5s was tight enough that a full-solution
    // `dotnet test` run (every assembly in parallel, CPU saturated) would
    // intermittently trip the timeout on the longer scripts even though the
    // session completes fine when the test runs in isolation. Bumped
    // 30s -> 60s (2026-09-05): even 30s still intermittently flaked in CI
    // (windows-latest, shared/constrained cores) under that same
    // cross-assembly contention — see .github/workflows/ci.yml's `-m:1` on
    // the Test step, which addresses the actual cause (dotnet test running
    // every test *project* in the solution as concurrent processes); this
    // wider margin is just a belt-and-suspenders on top of that fix, since
    // a real-socket test racing a wall clock should stay generous regardless.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"ct-telnettest-{Guid.NewGuid():N}.db");
    private readonly ServerStore _store;
    private readonly SharedGame _game;
    private readonly TcpListener _listener;

    public TelnetConnectionTests()
    {
        _store = new ServerStore(_dbPath);
        var world = TestTimeWorld.Build(seed: 9191);
        _game = new SharedGame(world, Array.Empty<Core.Characters.Traveler>(), new SystemRandomSource());
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Stop();
        _store.Dispose();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    /// <summary>Accepts one connection and runs a TelnetConnection against it on a background thread; returns that Task so tests can await it after "quit".</summary>
    private Task AcceptOneAndRun()
    {
        return Task.Run(async () =>
        {
            using var serverSide = await _listener.AcceptTcpClientAsync();
            var conn = new TelnetConnection(serverSide, _game, _store, worldSeed: 9191, log: _ => { });
            conn.Run();
        });
    }

    private async Task<TcpClient> ConnectClient()
    {
        var client = new TcpClient();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        await client.ConnectAsync(IPAddress.Loopback, port);
        client.ReceiveTimeout = (int)Timeout.TotalMilliseconds;
        return client;
    }

    private static async Task<string> ReadUntil(NetworkStream stream, string marker)
    {
        var buffer = new byte[4096];
        var sb = new StringBuilder();
        var deadline = DateTime.UtcNow + Timeout;

        while (DateTime.UtcNow < deadline)
        {
            var readTask = stream.ReadAsync(buffer.AsMemory(0, buffer.Length)).AsTask();
            var completed = await Task.WhenAny(readTask, Task.Delay(Timeout));
            if (completed != readTask)
            {
                throw new TimeoutException($"Timed out waiting for '{marker}'. Received so far:\n{sb}");
            }

            var read = await readTask;
            if (read <= 0)
            {
                throw new IOException($"Connection closed before '{marker}' arrived. Received so far:\n{sb}");
            }

            sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
            if (sb.ToString().Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return sb.ToString();
            }
        }

        throw new TimeoutException($"Timed out waiting for '{marker}'. Received so far:\n{sb}");
    }

    private static async Task Send(NetworkStream stream, string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        await stream.WriteAsync(bytes);
    }

    [Fact]
    public async Task FullSession_CreateAccount_CreateCharacter_RunACommand_Quit_SavesTheCharacter()
    {
        var serverTask = AcceptOneAndRun();
        using var client = await ConnectClient();
        await using var stream = client.GetStream();

        await ReadUntil(stream, "shared timeline");
        await ReadUntil(stream, "Account name:");

        await Send(stream, "NewVoyager");
        await ReadUntil(stream, "Set a password");

        await Send(stream, "hunter2pass");
        await ReadUntil(stream, "Confirm password:");

        await Send(stream, "hunter2pass");
        await ReadUntil(stream, "New Traveler");

        await Send(stream, "1"); // the "New Traveler" option — only entry when no saves exist
        await ReadUntil(stream, "Name your Traveler:");

        await Send(stream, "Rook");
        await ReadUntil(stream, "Choose your role:");

        await Send(stream, "1"); // first offered class
        await ReadUntil(stream, "now");

        // Joining renders the starting room to the new player.
        await ReadUntil(stream, "A.D.");

        await Send(stream, "quit");
        await ReadUntil(stream, "Farewell, Traveler. Progress saved.");

        await serverTask.WaitAsync(Timeout);

        var saved = _store.CharactersFor("NewVoyager");
        Assert.Single(saved);
        Assert.Equal("Rook", saved[0].Name);
        Assert.Equal(0, _game.OnlineCount);
    }

    [Fact]
    public async Task WrongPassword_IsRejected_ButASubsequentCorrectAttemptSucceeds()
    {
        _store.CreateAccount("Returning", "correctpass");
        var serverTask = AcceptOneAndRun();
        using var client = await ConnectClient();
        await using var stream = client.GetStream();

        await ReadUntil(stream, "Account name:");
        await Send(stream, "Returning");
        await ReadUntil(stream, "Password:");

        await Send(stream, "wrongpass");
        await ReadUntil(stream, "Wrong password.");

        // Second attempt, same connection — Login() allows up to 4 tries.
        await ReadUntil(stream, "Account name:");
        await Send(stream, "Returning");
        await ReadUntil(stream, "Password:");

        await Send(stream, "correctpass");
        await ReadUntil(stream, "Welcome back, Returning.");

        // Bail out via EOF (close the socket) rather than walking the rest
        // of character select — this test is only about the login retry.
        client.Close();
        await serverTask.WaitAsync(Timeout);
    }

    [Fact]
    public async Task FourBadAccountNames_ExhaustsTheAttemptBudget_AndClosesTheConnection()
    {
        var serverTask = AcceptOneAndRun();
        using var client = await ConnectClient();
        await using var stream = client.GetStream();

        await ReadUntil(stream, "Account name:");

        for (var i = 0; i < 3; i++)
        {
            await Send(stream, "x"); // too short — 3-20 chars required
            await ReadUntil(stream, "Account name:");
        }

        await Send(stream, "x"); // 4th and final attempt
        await ReadUntil(stream, "Too many attempts. Bye.");

        await serverTask.WaitAsync(Timeout);
        Assert.Equal(0, _game.OnlineCount);
    }
}
