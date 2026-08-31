using Mutants.Core.Items;
using Mutants.Core.Monsters;
using Mutants.Core.World;

namespace Mutants.Core.Time;

/// <summary>
/// The live, mutable population of one year — the monsters currently
/// standing in its rooms, plus loot lying on the ground. Seeded
/// deterministically from the world seed on first entry to a year and
/// then driven by <c>Mutants.Engine.Npc.MonsterController</c> (wander,
/// infight, heal). It lives inside <see cref="YearContent"/>, which
/// <see cref="TimeWorld.GetYear"/> memoizes for the session — so
/// revisiting a year this session shows the same monsters where you left
/// them, but nothing here is written to the save (a fresh session
/// re-seeds).
/// </summary>
public sealed class YearPopulation
{
    private readonly List<Monster> _monsters;
    private readonly Dictionary<Coordinate, List<Item>> _groundLoot = [];

    /// <summary>Living and dead — callers should filter on <c>!m.Health.IsDead</c> and remove on kill. Excludes the <see cref="Gatekeeper"/>.</summary>
    public IReadOnlyList<Monster> Monsters => _monsters;

    /// <summary>The Gatekeeper standing at the map's start room in a Gatekeeper year (see <see cref="GatekeeperSchedule"/>), or null. Kept out of <see cref="Monsters"/> so it never wanders or infights.</summary>
    public Monster? Gatekeeper { get; }

    /// <summary>Target population — the respawn trickle tops back up toward this, never past it.</summary>
    public int SoftCap { get; }

    /// <summary>Bookkeeping for the respawn trickle in MonsterController.</summary>
    public int TicksSinceRespawn { get; set; }

    private YearPopulation(List<Monster> monsters, Monster? gatekeeper, int softCap)
    {
        _monsters = monsters;
        Gatekeeper = gatekeeper;
        SoftCap = softCap;
    }

    /// <summary>
    /// Places <c>max(2, roomCount / 3)</c> monsters (roster factories
    /// picked at random) in distinct non-start rooms, deterministically
    /// from <paramref name="worldSeed"/> + <paramref name="year"/>. If
    /// <paramref name="gatekeeperFactory"/> is non-null its monster is
    /// built and stationed at the map's start room.
    /// </summary>
    public static YearPopulation Seed(
        long worldSeed,
        int year,
        LevelMap map,
        IReadOnlyList<Func<Monster>> roster,
        Func<Monster>? gatekeeperFactory)
    {
        var rng = DeterministicRandom.For(worldSeed, year, "monsters");

        var rooms = map.Rooms.Keys
            .Where(c => !c.Equals(map.Start))
            .OrderBy(c => c.North).ThenBy(c => c.East)
            .ToList();

        // Fisher–Yates with the deterministic rng, then take the front.
        for (var i = rooms.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (rooms[i], rooms[j]) = (rooms[j], rooms[i]);
        }

        var count = roster.Count == 0
            ? 0
            : Math.Min(rooms.Count, Math.Max(2, map.RoomCount / 3));

        var monsters = new List<Monster>(count);
        for (var i = 0; i < count; i++)
        {
            var monster = roster[rng.Next(roster.Count)]();
            monster.PlaceAt(rooms[i]);
            monsters.Add(monster);
        }

        Monster? gatekeeper = null;
        if (gatekeeperFactory is not null)
        {
            gatekeeper = gatekeeperFactory();
            gatekeeper.PlaceAt(map.Start);
        }

        return new YearPopulation(monsters, gatekeeper, count);
    }

    /// <summary>Living monsters standing at <paramref name="coordinate"/> (never the Gatekeeper).</summary>
    public IEnumerable<Monster> MonstersAt(Coordinate coordinate) =>
        _monsters.Where(m => !m.Health.IsDead && m.Position.Equals(coordinate));

    public bool HasLivingMonsterAt(Coordinate coordinate) =>
        _monsters.Any(m => !m.Health.IsDead && m.Position.Equals(coordinate));

    public void AddMonster(Monster monster) => _monsters.Add(monster);

    public void RemoveMonster(Monster monster) => _monsters.Remove(monster);

    public void AddGroundLoot(Coordinate coordinate, Item item)
    {
        if (!_groundLoot.TryGetValue(coordinate, out var pile))
        {
            pile = [];
            _groundLoot[coordinate] = pile;
        }

        pile.Add(item);
    }

    /// <summary>Items on the ground at <paramref name="coordinate"/> — empty if none.</summary>
    public IReadOnlyList<Item> LootAt(Coordinate coordinate) =>
        _groundLoot.TryGetValue(coordinate, out var pile) ? pile : [];

    /// <summary>Removes and returns the first ground-loot item at <paramref name="coordinate"/> matching <paramref name="match"/>, or null.</summary>
    public Item? TakeGroundLoot(Coordinate coordinate, Func<Item, bool> match)
    {
        if (!_groundLoot.TryGetValue(coordinate, out var pile))
        {
            return null;
        }

        var index = pile.FindIndex(new Predicate<Item>(match));
        if (index < 0)
        {
            return null;
        }

        var item = pile[index];
        pile.RemoveAt(index);
        if (pile.Count == 0)
        {
            _groundLoot.Remove(coordinate);
        }

        return item;
    }
}
