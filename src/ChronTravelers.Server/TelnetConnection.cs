using System.Net.Sockets;
using System.Text;
using ChronTravelers.Core.Characters;
using ChronTravelers.Engine.Persistence;
using ChronTravelers.Game;

namespace ChronTravelers.Server;

/// <summary>
/// One telnet client, start to finish: strip IAC negotiation off the
/// input, run the login + character-select flow, hand the character to the
/// <see cref="SharedGame"/>, then pump lines between the socket and
/// <see cref="SharedGame.Execute"/> until the player quits or drops.
/// </summary>
internal sealed class TelnetConnection
{
    private readonly TcpClient _client;
    private readonly SharedGame _game;
    private readonly ServerStore _store;
    private readonly long _worldSeed;
    private readonly Action<string> _log;

    private NetworkStream _stream = null!;
    private SocketOutput _out = null!;

    public TelnetConnection(TcpClient client, SharedGame game, ServerStore store, long worldSeed, Action<string> log)
    {
        _client = client;
        _game = game;
        _store = store;
        _worldSeed = worldSeed;
        _log = log;
    }

    public void Run()
    {
        var endpoint = _client.Client.RemoteEndPoint?.ToString() ?? "?";
        Session? session = null;
        string? account = null;
        try
        {
            _client.NoDelay = true;
            _stream = _client.GetStream();
            _out = new SocketOutput(_stream);

            _out.Line("");
            _out.Line("  C H R O N T R A V E L E R S  —  shared timeline");
            _out.Line("  the tunnel is still open.");
            _out.Line("");

            account = Login();
            if (account is null)
            {
                return;
            }

            _log($"{endpoint} logged in as {account}");

            var player = SelectOrCreateCharacter(account);
            if (player is null)
            {
                return;
            }

            session = _game.Join(account, player, _out);
            _log($"{account}/{player.Name} joined ({_game.OnlineCount} online)");

            while (true)
            {
                var line = ReadLine();
                if (line is null)
                {
                    break;
                }

                var trimmed = line.Trim();
                if (trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    _out.Line("Farewell, Traveler. Progress saved.");
                    break;
                }

                _game.Execute(session, trimmed);
            }
        }
        catch (IOException) { /* client dropped */ }
        catch (ObjectDisposedException) { /* socket closed */ }
        catch (Exception ex)
        {
            _log($"{endpoint} error: {ex.Message}");
        }
        finally
        {
            if (session is not null && account is not null)
            {
                try
                {
                    _store.SaveCharacter(account, session.Player, _worldSeed);
                    _game.Leave(session);
                    _log($"{account}/{session.Player.Name} left ({_game.OnlineCount} online)");
                }
                catch (Exception ex)
                {
                    _log($"save/leave failed for {account}: {ex.Message}");
                }
            }

            try { _client.Close(); } catch { /* ignore */ }
        }
    }

    // --- login ---------------------------------------------------------------

    private string? Login()
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var name = Prompt("Account name: ");
            if (name is null)
            {
                return null;
            }

            name = name.Trim();
            if (name.Length is < 3 or > 20 || !name.All(c => char.IsLetterOrDigit(c) || c is '_' or '-'))
            {
                _out.Line("Names are 3–20 chars, letters/digits/_/- only.");
                continue;
            }

            var existing = _store.FindAccount(name);
            if (existing is not null)
            {
                var pw = Prompt("Password: ");
                if (pw is null)
                {
                    return null;
                }

                if (PasswordHash.Verify(pw, existing.Salt, existing.Hash))
                {
                    _out.Line($"Welcome back, {existing.DisplayName}.");
                    return existing.DisplayName;
                }

                _out.Line("Wrong password.");
                continue;
            }

            _out.Line($"No account '{name}' — creating one.");
            var newPw = Prompt("Set a password (min 6 chars): ");
            if (newPw is null)
            {
                return null;
            }

            if (newPw.Length < 6)
            {
                _out.Line("Too short.");
                continue;
            }

            var confirm = Prompt("Confirm password: ");
            if (confirm != newPw)
            {
                _out.Line("Didn't match.");
                continue;
            }

            var created = _store.CreateAccount(name, newPw);
            _out.Line($"Account '{created.DisplayName}' created.");
            return created.DisplayName;
        }

        _out.Line("Too many attempts. Bye.");
        return null;
    }

    // --- character select --------------------------------------------------

    private Traveler? SelectOrCreateCharacter(string account)
    {
        var saved = _store.CharactersFor(account);

        _out.Line("");
        if (saved.Count > 0)
        {
            _out.Line("Your Travelers:");
            for (var i = 0; i < saved.Count; i++)
            {
                var c = saved[i];
                _out.Line($"  {i + 1}. {c.Name} the {c.Class} — level {c.Level}, furthest {c.FurthestYearReached} A.D.");
            }
        }
        else
        {
            _out.Line("You have no Travelers yet.");
        }

        _out.Line($"  {saved.Count + 1}. New Traveler (a role you haven't played)");

        while (true)
        {
            var pick = Prompt("> ");
            if (pick is null)
            {
                return null;
            }

            pick = pick.Trim();
            if (int.TryParse(pick, out var n))
            {
                if (n >= 1 && n <= saved.Count)
                {
                    var data = _store.LoadCharacter(account, saved[n - 1].Name)!;
                    _out.Line($"Continuing {data.Name} the {data.Class}.");
                    return CharacterMapper.FromSaveData(data);
                }

                if (n == saved.Count + 1)
                {
                    return CreateNew(account, saved);
                }
            }

            _out.Line($"Pick 1–{saved.Count + 1}.");
        }
    }

    private Traveler? CreateNew(string account, IReadOnlyList<CharacterSaveData> saved)
    {
        string name;
        while (true)
        {
            var entered = Prompt("Name your Traveler: ");
            if (entered is null)
            {
                return null;
            }

            entered = entered.Trim();
            if (entered.Length is < 2 or > 20)
            {
                _out.Line("2–20 characters.");
                continue;
            }

            if (saved.Any(c => string.Equals(c.Name, entered, StringComparison.OrdinalIgnoreCase)))
            {
                _out.Line("You already have a Traveler by that name.");
                continue;
            }

            name = entered;
            break;
        }

        var offered = CharacterFactory.OfferedClasses(saved);

        _out.Line("Choose your role:");
        for (var i = 0; i < offered.Count; i++)
        {
            _out.Line($"  {i + 1}. {offered[i]}");
        }

        while (true)
        {
            var pick = Prompt("> ");
            if (pick is null)
            {
                return null;
            }

            if (int.TryParse(pick.Trim(), out var n) && n >= 1 && n <= offered.Count)
            {
                var traveler = CharacterFactory.NewTraveler(name, offered[n - 1]);
                _out.Line($"You're the {traveler.Class} now. Downstream is the only direction that means anything.");
                return traveler;
            }

            _out.Line($"Pick 1–{offered.Count}.");
        }
    }

    // --- raw telnet I/O --------------------------------------------------

    private string? Prompt(string text)
    {
        _out.Raw(text);
        return ReadLine();
    }

    private readonly List<byte> _lineBuffer = [];
    private readonly byte[] _readChunk = new byte[512];

    /// <summary>Blocking line read with IAC (telnet negotiation) bytes stripped. Null on EOF/close.</summary>
    private string? ReadLine()
    {
        while (true)
        {
            for (var i = 0; i < _lineBuffer.Count; i++)
            {
                if (_lineBuffer[i] == (byte)'\n')
                {
                    var lineBytes = _lineBuffer.Take(i).Where(b => b != (byte)'\r').ToArray();
                    _lineBuffer.RemoveRange(0, i + 1);
                    return Encoding.UTF8.GetString(lineBytes);
                }
            }

            int read;
            try
            {
                read = _stream.Read(_readChunk, 0, _readChunk.Length);
            }
            catch
            {
                return null;
            }

            if (read <= 0)
            {
                return null;
            }

            for (var i = 0; i < read; i++)
            {
                var b = _readChunk[i];
                if (b == 255) // IAC
                {
                    if (i + 1 >= read)
                    {
                        break;
                    }

                    var cmd = _readChunk[++i];
                    if (cmd is >= 251 and <= 254 && i + 1 < read)
                    {
                        i++; // WILL/WONT/DO/DONT + option byte
                    }
                    // SB(250)…SE and bare commands: just skip the marker; good enough here.
                    continue;
                }

                _lineBuffer.Add(b);
            }
        }
    }

    /// <summary>Thread-safe line/raw writer over the socket — the game tick and this connection's REPL both write.</summary>
    private sealed class SocketOutput(NetworkStream stream) : IGameOutput
    {
        private readonly object _writeGate = new();

        public void Line(string text) => Write(text + "\r\n");

        public void Raw(string text) => Write(text);

        private void Write(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            lock (_writeGate)
            {
                try
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();
                }
                catch
                {
                    // client gone — the read loop will notice and tear down.
                }
            }
        }
    }
}
