namespace ChronoTravelers.Game;

/// <summary>
/// Where a <see cref="Session"/>'s output goes — one line at a time, plain
/// text (no console-only markup). A TCP/telnet front end writes to the
/// socket; a test writes to a list; a future SignalR hub pushes to the
/// client. The game layer never touches a transport directly.
/// </summary>
public interface IGameOutput
{
    void Line(string text);
}
