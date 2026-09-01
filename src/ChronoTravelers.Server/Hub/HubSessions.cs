using System.Collections.Concurrent;
using System.Threading.Channels;
using ChronoTravelers.Game;
using Microsoft.AspNetCore.SignalR;

namespace ChronoTravelers.Server.Hub;

/// <summary>
/// Per-SignalR-connection state, keyed by connection id — the account
/// name, the joined <see cref="Session"/> (once past the handshake), and
/// an ordered outbox so a burst of <see cref="IGameOutput.Line"/> calls
/// under the SharedGame lock never blocks on the socket and always arrives
/// in order.
/// </summary>
public sealed class HubSessions
{
    private readonly ConcurrentDictionary<string, HubConn> _byConnection = new();

    public HubConn Open(string connectionId, IHubContext<GameHub> hub)
    {
        var conn = new HubConn(connectionId, hub);
        _byConnection[connectionId] = conn;
        return conn;
    }

    public HubConn? Get(string connectionId) =>
        _byConnection.TryGetValue(connectionId, out var c) ? c : null;

    public bool Remove(string connectionId, out HubConn? conn) =>
        _byConnection.TryRemove(connectionId, out conn);
}

public sealed class HubConn : IGameOutput, IAsyncDisposable
{
    private readonly string _connectionId;
    private readonly IHubContext<GameHub> _hub;
    private readonly Channel<string> _outbox = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _pump;

    public string? Account { get; set; }
    public Session? Session { get; set; }

    public HubConn(string connectionId, IHubContext<GameHub> hub)
    {
        _connectionId = connectionId;
        _hub = hub;
        _pump = Task.Run(PumpAsync);
    }

    public void Line(string text) => _outbox.Writer.TryWrite(text);

    private async Task PumpAsync()
    {
        try
        {
            await foreach (var line in _outbox.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    await _hub.Clients.Client(_connectionId).SendAsync("Receive", line).ConfigureAwait(false);
                }
                catch
                {
                    // client gone mid-send — OnDisconnectedAsync will clean up.
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        _outbox.Writer.TryComplete();
        try { await _pump.ConfigureAwait(false); } catch { /* ignore */ }
    }
}
