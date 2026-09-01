using ChronoTravelers.Core.Characters;
using ChronoTravelers.Engine.Simulation;

namespace ChronoTravelers.Game;

/// <summary>
/// One connected player in a <see cref="SharedGame"/> — an account name, a
/// live <see cref="Traveler"/>, and the sink its output goes to. All game
/// mutation for this session runs under the SharedGame lock.
/// </summary>
public sealed class Session
{
    public Guid Id { get; } = Guid.NewGuid();
    public string AccountName { get; }
    public Traveler Player { get; }
    public IGameOutput Output { get; }
    public DateTime JoinedUtc { get; } = DateTime.UtcNow;

    /// <summary>How many broadcast events this session has already been shown (so it never sees the backlog or a duplicate).</summary>
    internal int ShownBroadcast { get; set; }

    /// <summary>Threaded through <see cref="WorldSimulation.TickMultiplayer"/> each world tick.</summary>
    internal PlayerTickState TickState { get; }

    public Session(string accountName, Traveler player, IGameOutput output)
    {
        AccountName = accountName;
        Player = player;
        Output = output;
        TickState = new PlayerTickState { Player = player };
    }

    public void Send(string text) => Output.Line(text);
}
