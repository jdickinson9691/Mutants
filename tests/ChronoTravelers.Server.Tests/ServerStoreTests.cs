using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;

namespace ChronoTravelers.Server.Tests;

/// <summary>
/// LiteDB-backed accounts + characters (ServerStore.cs). Each test opens
/// its own throwaway .db file so tests never see each other's data or race
/// on a shared file, and deletes it afterwards.
/// </summary>
public class ServerStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"ct-servertest-{Guid.NewGuid():N}.db");
    private ServerStore? _store;

    private ServerStore Store => _store ??= new ServerStore(_path);

    public void Dispose()
    {
        _store?.Dispose();
        try { File.Delete(_path); } catch { /* best effort cleanup */ }
    }

    [Fact]
    public void FindAccount_ForAnUnknownName_ReturnsNull()
    {
        Assert.Null(Store.FindAccount("nobody"));
    }

    [Fact]
    public void CreateAccount_ThenFindAccount_RoundTrips()
    {
        var created = Store.CreateAccount("Voyager", "hunter2pass");

        var found = Store.FindAccount("Voyager");

        Assert.NotNull(found);
        Assert.Equal("Voyager", found!.DisplayName);
        Assert.Equal(created.Salt, found.Salt);
        Assert.Equal(created.Hash, found.Hash);
    }

    [Fact]
    public void CreateAccount_HashesThePassword_NeverStoresItInThePlain()
    {
        var created = Store.CreateAccount("Voyager", "hunter2pass");

        Assert.DoesNotContain("hunter2pass", created.Hash);
        Assert.DoesNotContain("hunter2pass", created.Salt);
        Assert.True(PasswordHash.Verify("hunter2pass", created.Salt, created.Hash));
    }

    [Fact]
    public void FindAccount_IsCaseInsensitiveAndTrimsWhitespace()
    {
        Store.CreateAccount("Voyager", "hunter2pass");

        Assert.NotNull(Store.FindAccount("voyager"));
        Assert.NotNull(Store.FindAccount("VOYAGER"));
        Assert.NotNull(Store.FindAccount("  Voyager  "));
    }

    [Fact]
    public void CreateAccount_WithADuplicateName_Throws()
    {
        Store.CreateAccount("Voyager", "hunter2pass");

        // ServerStore itself doesn't pre-check for duplicates (GameHub.Login
        // does, by calling FindAccount first) — the unique index on Key is
        // the actual backstop, and it should surface as a thrown exception
        // rather than silently overwriting the existing account.
        Assert.ThrowsAny<Exception>(() => Store.CreateAccount("voyager", "differentpass"));
    }

    [Fact]
    public void CharactersFor_WithNoCharacters_ReturnsEmpty()
    {
        Store.CreateAccount("Voyager", "hunter2pass");

        Assert.Empty(Store.CharactersFor("Voyager"));
    }

    [Fact]
    public void SaveCharacter_ThenCharactersFor_ReturnsIt()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);

        Store.SaveCharacter("Voyager", traveler, worldSeed: 42);

        var chars = Store.CharactersFor("Voyager");
        Assert.Single(chars);
        Assert.Equal("Rook", chars[0].Name);
        Assert.Equal(42, chars[0].WorldSeed);
    }

    [Fact]
    public void SaveCharacter_CalledAgainForTheSameName_UpdatesRatherThanDuplicates()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        Store.SaveCharacter("Voyager", traveler, worldSeed: 1);

        traveler.GainXp(500);
        Store.SaveCharacter("Voyager", traveler, worldSeed: 1);

        var chars = Store.CharactersFor("Voyager");
        Assert.Single(chars);
    }

    [Fact]
    public void SaveCharacter_ScopesCharactersToTheirOwningAccount()
    {
        var rook = new Traveler("Rook", CharacterClass.Soldier);
        var scout = new Traveler("Scout", CharacterClass.Soldier);

        Store.SaveCharacter("Voyager", rook, worldSeed: 1);
        Store.SaveCharacter("Nomad", scout, worldSeed: 1);

        Assert.Single(Store.CharactersFor("Voyager"));
        Assert.Single(Store.CharactersFor("Nomad"));
        Assert.Equal("Rook", Store.CharactersFor("Voyager")[0].Name);
        Assert.Equal("Scout", Store.CharactersFor("Nomad")[0].Name);
    }

    [Fact]
    public void CharactersFor_OrdersByNameCaseInsensitively()
    {
        Store.SaveCharacter("Voyager", new Traveler("Zed", CharacterClass.Soldier), worldSeed: 1);
        Store.SaveCharacter("Voyager", new Traveler("alpha", CharacterClass.Soldier), worldSeed: 1);
        Store.SaveCharacter("Voyager", new Traveler("Mid", CharacterClass.Soldier), worldSeed: 1);

        var names = Store.CharactersFor("Voyager").Select(c => c.Name).ToList();

        Assert.Equal(["alpha", "Mid", "Zed"], names);
    }

    [Fact]
    public void LoadCharacter_ForANameThatDoesNotExist_ReturnsNull()
    {
        Store.SaveCharacter("Voyager", new Traveler("Rook", CharacterClass.Soldier), worldSeed: 1);

        Assert.Null(Store.LoadCharacter("Voyager", "Nobody"));
    }

    [Fact]
    public void LoadCharacter_DoesNotLeakAcrossAccounts()
    {
        Store.SaveCharacter("Voyager", new Traveler("Rook", CharacterClass.Soldier), worldSeed: 1);

        Assert.Null(Store.LoadCharacter("Nomad", "Rook"));
    }

    [Fact]
    public void Dispose_ThenReopeningTheSameFile_StillSeesTheData()
    {
        Store.SaveCharacter("Voyager", new Traveler("Rook", CharacterClass.Soldier), worldSeed: 1);
        _store!.Dispose();
        _store = null;

        using var reopened = new ServerStore(_path);
        Assert.Single(reopened.CharactersFor("Voyager"));
    }
}
