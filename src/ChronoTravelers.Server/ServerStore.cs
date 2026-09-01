using ChronoTravelers.Core.Characters;
using ChronoTravelers.Engine.Persistence;
using LiteDB;

namespace ChronoTravelers.Server;

/// <summary>
/// The server's own LiteDB store: accounts (name + PBKDF2 hash) and the
/// characters that belong to them. A character save is the same
/// <see cref="CharacterSaveData"/> shape the console uses, just scoped to
/// an account rather than living in a per-machine file.
/// </summary>
public sealed class ServerStore : IDisposable
{
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<AccountRecord> _accounts;
    private readonly ILiteCollection<CharacterRecord> _characters;
    private readonly object _gate = new();

    public ServerStore(string path)
    {
        _db = new LiteDatabase($"Filename={path};Connection=shared");
        _accounts = _db.GetCollection<AccountRecord>("accounts");
        _accounts.EnsureIndex(a => a.Key, unique: true);
        _characters = _db.GetCollection<CharacterRecord>("characters");
        _characters.EnsureIndex(c => c.Account);
    }

    private static string Key(string name) => name.Trim().ToLowerInvariant();

    public AccountRecord? FindAccount(string name)
    {
        lock (_gate) { return _accounts.FindOne(a => a.Key == Key(name)); }
    }

    public AccountRecord CreateAccount(string name, string password)
    {
        var (salt, hash) = PasswordHash.Create(password);
        var rec = new AccountRecord { Key = Key(name), DisplayName = name.Trim(), Salt = salt, Hash = hash, CreatedUtc = DateTime.UtcNow };
        lock (_gate) { _accounts.Insert(rec); }
        return rec;
    }

    public IReadOnlyList<CharacterSaveData> CharactersFor(string account)
    {
        lock (_gate)
        {
            return _characters.Find(c => c.Account == Key(account))
                .Select(c => c.Data)
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public CharacterSaveData? LoadCharacter(string account, string characterName)
    {
        lock (_gate)
        {
            return _characters
                .FindOne(c => c.Account == Key(account) && c.Data.Name == characterName)
                ?.Data;
        }
    }

    /// <summary>Upserts a character under its account, keyed by (account, character name).</summary>
    public void SaveCharacter(string account, Traveler traveler, long worldSeed)
    {
        var data = CharacterMapper.ToSaveData(traveler, worldSeed);
        lock (_gate)
        {
            var existing = _characters.FindOne(c => c.Account == Key(account) && c.Data.Name == traveler.Name);
            if (existing is null)
            {
                _characters.Insert(new CharacterRecord { Account = Key(account), Data = data });
            }
            else
            {
                existing.Data = data;
                _characters.Update(existing);
            }
        }
    }

    public void Dispose() => _db.Dispose();

    public sealed class AccountRecord
    {
        public ObjectId Id { get; set; } = ObjectId.NewObjectId();
        public string Key { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Salt { get; set; } = "";
        public string Hash { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
    }

    public sealed class CharacterRecord
    {
        public ObjectId Id { get; set; } = ObjectId.NewObjectId();
        public string Account { get; set; } = "";
        public CharacterSaveData Data { get; set; } = new();
    }
}
