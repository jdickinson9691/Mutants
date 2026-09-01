using ChronTravelers.Core.Items;
using ChronTravelers.Core.Monsters;
using ChronTravelers.Core.World;

namespace ChronTravelers.Core.Time;

/// <summary>
/// The live, mutable population of one year — the monsters currently
/// standing in its rooms, plus loot lying on the ground. Seeded
/// deterministically from the world seed on first entry to a year and
/// then driven by <c>ChronTravelers.Engine.Npc.MonsterController</c> (wander,
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

    /// <summary>Living and dead — callers should filter on <c>!m.Health.IsDead</c> and remove on kill. Excludes the <see cref="Warden"/>.</summary>
    public IReadOnlyList<Monster> Monsters => _monsters;

    /// <summary>The Warden standing at the map's start room in a Warden year (see <see cref="WardenSchedule"/>), or null. Kept out of <see cref="Monsters"/> so it never wanders or infights.</summary>
    public Monster? Warden { get; }

    /// <summary>Target population — the respawn trickle tops back up toward this, never past it.</summary>
    public int SoftCap { get; }

    /// <summary>Bookkeeping for the respawn trickle in MonsterController.</summary>
    public int TicksSinceRespawn { get; set; }

    /// <summary>
    /// Bookkeeping for the ambush cooldown in MonsterController — so
    /// info-checking near a monster isn't death by a thousand cuts. Starts
    /// primed (a lingering player is ambushable on their first eligible
    /// tick; the grace period already covered the turn they arrived).
    /// </summary>
    public int TicksSinceAmbush { get; set; } = 2;

    private YearPopulation(List<Monster> monsters, Monster? warden, int softCap)
    {
        _monsters = monsters;
        Warden = warden;
        SoftCap = softCap;
    }

    /// <summary>~55% of years seed a single apex; a rare few seed two.</summary>
    private const double SecondApexChance = 0.20;

    /// <summary>
    /// Places <c>max(4, roomCount * 2/5)</c> monsters (roster factories
    /// picked at random) in distinct non-start rooms, deterministically
    /// from <paramref name="worldSeed"/> + <paramref name="year"/>. The
    /// floor of 4 keeps the small (~9-room) maps from feeling deserted once
    /// infighting thins them; the respawn trickle in
    /// <c>MonsterController</c> tops back up toward this count. If
    /// <paramref name="apexRoster"/> is non-empty, 0–2 apex monsters
    /// (<see cref="Monster.IsApex"/>) are placed in further rooms alongside
    /// the regular population. If <paramref name="wardenFactory"/> is
    /// non-null its monster is built and stationed at the map's start room.
    /// If <paramref name="floorLootFactory"/> is non-null, ~a third of the
    /// grid's rooms get a random item on load; if
    /// <paramref name="timeShardFactory"/> is non-null, one further room
    /// gets a single Time Shard.
    /// </summary>
    public static YearPopulation Seed(
        long worldSeed,
        int year,
        LevelMap map,
        IReadOnlyList<Func<Monster>> roster,
        Func<Monster>? wardenFactory,
        IReadOnlyList<Func<Monster>>? apexRoster = null,
        Func<Item>? floorLootFactory = null,
        Func<Item>? timeShardFactory = null)
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
            : Math.Min(rooms.Count, Math.Max(4, map.RoomCount * 2 / 5));

        var monsters = new List<Monster>(count);
        for (var i = 0; i < count; i++)
        {
            var monster = roster[rng.Next(roster.Count)]();
            monster.PlaceAt(rooms[i]);
            monsters.Add(monster);
        }

        // Apex monsters take the next free shuffled rooms — a rare, tougher
        // presence the player can choose to take on. Roll happens even when
        // there's no roster so the deterministic stream doesn't shift.
        var apexCount = 0;
        if (rng.NextDouble() < 0.55)
        {
            apexCount = rng.NextDouble() < SecondApexChance ? 2 : 1;
        }

        if (apexRoster is { Count: > 0 })
        {
            for (var i = 0; i < apexCount && count + i < rooms.Count; i++)
            {
                var apex = apexRoster[rng.Next(apexRoster.Count)]();
                apex.PlaceAt(rooms[count + i]);
                monsters.Add(apex);
            }
        }

        Monster? warden = null;
        if (wardenFactory is not null)
        {
            warden = wardenFactory();
            warden.PlaceAt(map.Start);
        }

        var population = new YearPopulation(monsters, warden, count);

        // --- floor loot -------------------------------------------------
        // A separate deterministic shuffle of every room (start included)
        // so a year never feels empty: one room gets the Time Shard, then
        // ~a third of the grid gets a random item.
        if (floorLootFactory is not null || timeShardFactory is not null)
        {
            var lootRng = DeterministicRandom.For(worldSeed, year, "floorloot-rooms");
            var lootRooms = map.Rooms.Keys.OrderBy(c => c.North).ThenBy(c => c.East).ToList();
            for (var i = lootRooms.Count - 1; i > 0; i--)
            {
                var j = lootRng.Next(i + 1);
                (lootRooms[i], lootRooms[j]) = (lootRooms[j], lootRooms[i]);
            }

            var next = 0;
            if (timeShardFactory is not null && next < lootRooms.Count)
            {
                population.AddGroundLoot(lootRooms[next++], timeShardFactory());
            }

            if (floorLootFactory is not null)
            {
                var itemRoomCount = Math.Max(1, (int)Math.Round(lootRooms.Count / 3.0));
                for (var i = 0; i < itemRoomCount && next < lootRooms.Count; i++, next++)
                {
                    population.AddGroundLoot(lootRooms[next], floorLootFactory());
                }
            }
        }

        return population;
    }

    /// <summary>Living monsters standing at <paramref name="coordinate"/> (never the Warden).</summary>
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
