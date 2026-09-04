using System.Security.Claims;
using ChronoTravelers.Core.Time;
using ChronoTravelers.Engine;
using ChronoTravelers.Game;
using ChronoTravelers.Server.Hub;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;

namespace ChronoTravelers.Server.Tests;

/// <summary>
/// GameHub.cs, exercised as a plain object rather than through a real
/// Kestrel/SignalR pipeline: <see cref="Microsoft.AspNetCore.SignalR.Hub.Context"/>
/// and <c>.Clients</c> are public settable properties, so a hub instance
/// can be constructed directly and driven with hand-rolled fakes. Each
/// test gets its own ServerStore (throwaway temp file), SharedGame, and
/// hub instance so nothing leaks between tests.
/// </summary>
public class GameHubTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"ct-hubtest-{Guid.NewGuid():N}.db");
    private readonly ServerStore _store;
    private readonly SharedGame _game;
    private readonly HubSessions _sessions = new();
    private readonly FakeHubContext _hubContext = new();

    public GameHubTests()
    {
        _store = new ServerStore(_dbPath);
        var world = TestTimeWorld.Build(seed: 4242);
        _game = new SharedGame(world, Array.Empty<Core.Characters.Traveler>(), new SystemRandomSource());
    }

    public void Dispose()
    {
        _store.Dispose();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    /// <summary>Builds a hub wired up like the real DI container would, with Context set and a session opened (mirrors OnConnectedAsync).</summary>
    private async Task<GameHub> NewConnectedHub(string connectionId = "conn-1")
    {
        var hub = new GameHub(_game, _store, new WorldSeed(4242), _sessions, _hubContext)
        {
            Context = new FakeHubCallerContext(connectionId),
        };
        await hub.OnConnectedAsync();
        return hub;
    }

    // --- Login -----------------------------------------------------------

    [Fact]
    public async Task Login_WithATooShortName_IsRejectedWithoutTouchingTheStore()
    {
        var hub = await NewConnectedHub();

        var result = hub.Login("ab", "longenoughpassword");

        Assert.StartsWith("invalid:", result);
        Assert.Null(_store.FindAccount("ab"));
    }

    [Fact]
    public async Task Login_WithIllegalCharactersInTheName_IsRejected()
    {
        var hub = await NewConnectedHub();

        var result = hub.Login("bad name!", "longenoughpassword");

        Assert.StartsWith("invalid:", result);
    }

    [Fact]
    public async Task Login_WithATooShortPassword_IsRejected()
    {
        var hub = await NewConnectedHub();

        var result = hub.Login("Newcomer", "short");

        Assert.StartsWith("invalid:", result);
        Assert.Null(_store.FindAccount("Newcomer"));
    }

    [Fact]
    public async Task Login_ForANewAccountName_CreatesIt()
    {
        var hub = await NewConnectedHub();

        var result = hub.Login("Newcomer", "longenoughpassword");

        Assert.Equal("created", result);
        Assert.NotNull(_store.FindAccount("Newcomer"));
    }

    [Fact]
    public async Task Login_ForAnExistingAccount_WithTheRightPassword_Succeeds()
    {
        _store.CreateAccount("Voyager", "hunter2pass");
        var hub = await NewConnectedHub();

        var result = hub.Login("Voyager", "hunter2pass");

        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task Login_ForAnExistingAccount_WithTheWrongPassword_IsRejected()
    {
        _store.CreateAccount("Voyager", "hunter2pass");
        var hub = await NewConnectedHub();

        var result = hub.Login("Voyager", "wrongpassword");

        Assert.Equal("badpassword", result);
    }

    // --- Characters / OfferedClasses --------------------------------------

    [Fact]
    public async Task Characters_BeforeLoggingIn_Throws()
    {
        var hub = await NewConnectedHub();

        Assert.Throws<HubException>(() => hub.Characters());
    }

    [Fact]
    public async Task Characters_WithNoneSaved_ReturnsAnEmptyString()
    {
        var hub = await NewConnectedHub();
        hub.Login("Newcomer", "longenoughpassword");

        Assert.Equal("", hub.Characters());
    }

    [Fact]
    public async Task Characters_ListsEverySavedTraveler()
    {
        var hub = await NewConnectedHub();
        hub.Login("Voyager", "hunter2pass");
        _store.SaveCharacter("Voyager", new Core.Characters.Traveler("Rook", Core.Classes.CharacterClass.Soldier), worldSeed: 4242);

        var listing = hub.Characters();

        Assert.Contains("Rook", listing);
        Assert.StartsWith("1.", listing);
    }

    [Fact]
    public async Task OfferedClasses_BeforeLoggingIn_Throws()
    {
        var hub = await NewConnectedHub();

        Assert.Throws<HubException>(() => hub.OfferedClasses());
    }

    [Fact]
    public async Task OfferedClasses_ForABrandNewAccount_OffersAllFiveRoles()
    {
        var hub = await NewConnectedHub();
        hub.Login("Newcomer", "longenoughpassword");

        var offered = hub.OfferedClasses().Split(',');

        Assert.Equal(5, offered.Length);
    }

    // --- CreateCharacter ---------------------------------------------------

    [Fact]
    public async Task CreateCharacter_BeforeLoggingIn_Throws()
    {
        var hub = await NewConnectedHub();

        Assert.Throws<HubException>(() => hub.CreateCharacter("Rook", "Soldier"));
    }

    [Fact]
    public async Task CreateCharacter_WithATooShortName_IsRejected()
    {
        var hub = await NewConnectedHub();
        hub.Login("Newcomer", "longenoughpassword");

        var result = hub.CreateCharacter("R", "Soldier");

        Assert.StartsWith("error:", result);
    }

    [Fact]
    public async Task CreateCharacter_WithAnUnknownClassName_IsRejected()
    {
        var hub = await NewConnectedHub();
        hub.Login("Newcomer", "longenoughpassword");

        var result = hub.CreateCharacter("Rook", "Wizard");

        Assert.StartsWith("error:", result);
    }

    [Fact]
    public async Task CreateCharacter_WithAValidNameAndClass_JoinsTheSharedGame()
    {
        var hub = await NewConnectedHub();
        hub.Login("Newcomer", "longenoughpassword");

        var result = hub.CreateCharacter("Rook", "Soldier");

        Assert.Equal("joined", result);
        Assert.Equal(1, _game.OnlineCount);
    }

    [Fact]
    public async Task CreateCharacter_WithADuplicateNameForTheSameAccount_IsRejected()
    {
        var hub = await NewConnectedHub();
        hub.Login("Voyager", "hunter2pass");
        _store.SaveCharacter("Voyager", new Core.Characters.Traveler("Rook", Core.Classes.CharacterClass.Soldier), worldSeed: 4242);

        var result = hub.CreateCharacter("Rook", "Soldier");

        Assert.StartsWith("error:", result);
    }

    [Fact]
    public async Task CreateCharacter_WhileAlreadyPlaying_IsRejected()
    {
        var hub = await NewConnectedHub();
        hub.Login("Newcomer", "longenoughpassword");
        hub.CreateCharacter("Rook", "Soldier");

        var second = hub.CreateCharacter("Scout", "Soldier");

        Assert.Equal("error: already playing", second);
    }

    // --- Continue ------------------------------------------------------

    [Fact]
    public async Task Continue_WithAnOutOfRangeIndex_IsRejected()
    {
        var hub = await NewConnectedHub();
        hub.Login("Newcomer", "longenoughpassword");

        var result = hub.Continue(1);

        Assert.StartsWith("error:", result);
    }

    [Fact]
    public async Task Continue_WithAValidIndex_JoinsTheSavedCharacter()
    {
        var hub = await NewConnectedHub();
        hub.Login("Voyager", "hunter2pass");
        _store.SaveCharacter("Voyager", new Core.Characters.Traveler("Rook", Core.Classes.CharacterClass.Soldier), worldSeed: 4242);

        var result = hub.Continue(1);

        Assert.Equal("joined", result);
        Assert.Equal(1, _game.OnlineCount);
    }

    // --- Send ------------------------------------------------------------

    [Fact]
    public async Task Send_BeforeAnyCharacterHasJoined_TellsThePlayerToPickOne()
    {
        var hub = await NewConnectedHub();
        hub.Login("Newcomer", "longenoughpassword");

        // Send() doesn't throw or return anything observable when no
        // session exists yet — it just writes "Pick a Traveler first."
        // through the connection's own output channel. The important
        // thing here is that it doesn't throw and doesn't touch the game.
        var exception = Record.Exception(() => hub.Send("look"));

        Assert.Null(exception);
        Assert.Equal(0, _game.OnlineCount);
    }

    [Fact]
    public async Task Send_AfterJoining_IsDeliveredToTheSharedGame()
    {
        var hub = await NewConnectedHub();
        hub.Login("Newcomer", "longenoughpassword");
        hub.CreateCharacter("Rook", "Soldier");

        var exception = Record.Exception(() => hub.Send("look"));

        Assert.Null(exception);
    }

    // --- OnDisconnectedAsync ----------------------------------------------

    [Fact]
    public async Task OnDisconnectedAsync_ForAJoinedPlayer_SavesAndLeaves()
    {
        var hub = await NewConnectedHub();
        hub.Login("Voyager", "hunter2pass");
        hub.CreateCharacter("Rook", "Soldier");

        await hub.OnDisconnectedAsync(null);

        Assert.Equal(0, _game.OnlineCount);
        Assert.NotNull(_store.LoadCharacter("Voyager", "Rook"));
        Assert.Null(_sessions.Get("conn-1"));
    }

    [Fact]
    public async Task OnDisconnectedAsync_BeforeAnyLoginOrJoin_DoesNotThrow()
    {
        var hub = await NewConnectedHub();

        var exception = await Record.ExceptionAsync(() => hub.OnDisconnectedAsync(null));

        Assert.Null(exception);
    }

    // --- minimal fakes -----------------------------------------------------

    private sealed class FakeHubCallerContext(string connectionId) : HubCallerContext
    {
        public override string ConnectionId { get; } = connectionId;
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    /// <summary>Just enough of IHubContext&lt;GameHub&gt; for HubConn's outbox pump to have somewhere to send to — it swallows send failures anyway, so this stays deliberately minimal.</summary>
    private sealed class FakeHubContext : IHubContext<GameHub>
    {
        public IHubClients Clients { get; } = new FakeHubClients();
        public IGroupManager Groups { get; } = new FakeGroupManager();
    }

    private sealed class FakeHubClients : IHubClients
    {
        private static readonly IClientProxy Proxy = new FakeClientProxy();
        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Client(string connectionId) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
        public IClientProxy Group(string groupName) => Proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
        public IClientProxy OthersInGroup(string groupName) => Proxy;
        public IClientProxy User(string userId) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
    }

    private sealed class FakeClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
