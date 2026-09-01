using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Time;
using ChronoTravelers.Engine.Content;

namespace ChronoTravelers.Engine.Tests.Content;

/// <summary>
/// Loads the actual shipped timeline catalogs in src/ChronoTravelers.Content
/// (monster-species / item-archetypes / eras / store-templates) into a
/// real <see cref="TimeWorld"/> and checks they stay internally
/// consistent as they're edited — the continuous-timeline counterpart to
/// <see cref="RealContentTests"/>.
///
/// Same xUnit caveat as RealContentTests: this machine's test-discovery
/// chokes on Assert.Contains(collection, predicate) and
/// Assert.Equal(IEnumerable, IEnumerable); HashSet / .Any() / Assert.All
/// below are a deliberate workaround.
/// </summary>
public class TimeWorldContentTests
{
    private const long TestSeed = 20250305L;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ChronoTravelers.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException(
            $"Could not locate repo root (ChronoTravelers.sln) walking up from {AppContext.BaseDirectory}.");
    }

    private static string RealContentDirectory() => Path.Combine(RepoRoot(), "src", "ChronoTravelers.Content");

    private static TimeWorld ShippedWorld() => ContentLoader.LoadTimeWorld(RealContentDirectory(), TestSeed);

    private static readonly int[] SampleYears = [2000, 2200, 2650, 3000, 3400, 3900, 4300, 4750, 5000];

    [Fact]
    public void ShippedTimelineContent_LoadsWithoutError()
    {
        var world = ShippedWorld();
        Assert.NotEmpty(world.Eras.Eras);
        Assert.Equal(2000, world.Eras.Eras[0].FromYear);
    }

    [Fact]
    public void EveryEraSpanIsCovered_AndEveryYearYieldsAWellFormedConnectedMap()
    {
        var world = ShippedWorld();

        foreach (var year in SampleYears)
        {
            var content = world.GetYear(year);
            Assert.Equal(new List<string>(), new List<string>(content.Map.Validate()));

            var reachable = ReachableRoomCount(content.Map);
            Assert.True(reachable == content.Map.RoomCount,
                $"Year {year}: only {reachable}/{content.Map.RoomCount} rooms reachable from start.");

            Assert.NotEmpty(content.MonsterRoster);
        }
    }

    [Fact]
    public void MonsterAndLootPowerRisesAcrossTheTimeline()
    {
        var world = ShippedWorld();
        var early = world.GetYear(2100);
        var late = world.GetYear(4800);

        Assert.True(late.Tier > early.Tier);
        Assert.True(late.MonsterRoster[0]().Health.Max > early.MonsterRoster[0]().Health.Max);
    }

    [Fact]
    public void WardenYears_AreSpacedFiftyToOneHundredYearsApart()
    {
        var years = ShippedWorld().WardenYears.ToList();
        Assert.NotEmpty(years);

        var previous = TimeScale.MinYear;
        foreach (var year in years)
        {
            Assert.InRange(year - previous, WardenSchedule.MinGap, WardenSchedule.MaxGap);
            previous = year;
        }
    }

    [Fact]
    public void EveryWardenYear_YieldsAWardenGuardingALegendaryWeapon()
    {
        var world = ShippedWorld();

        foreach (var year in world.WardenYears)
        {
            var content = world.GetYear(year);
            Assert.NotNull(content.Warden);

            var monster = content.Warden!();
            var drop = Assert.Single(monster.LootTable);
            Assert.Equal(ItemType.Weapon, drop.Item.Type);
            Assert.Equal(Rarity.Legendary, drop.Item.Rarity);
        }
    }

    [Fact]
    public void EveryYearHasAGovernmentStoreCarryingEveryStapleKind()
    {
        var world = ShippedWorld();

        foreach (var year in SampleYears)
        {
            var government = world.GetYear(year).StoreSlots
                .Where(s => s.Store is { IsGovernmentRun: true })
                .Select(s => s.Store!)
                .SingleOrDefault();

            Assert.NotNull(government);
            var listings = government!.Listings;

            Assert.True(listings.Any(l => l.Item.ConsumableEffect == ConsumableEffectType.Heal), $"Year {year}: no heal item.");
            Assert.True(listings.Any(l => l.Item.ConsumableEffect == ConsumableEffectType.BuffAttack), $"Year {year}: no attack potion.");
            Assert.True(listings.Any(l => l.Item.ConsumableEffect == ConsumableEffectType.BuffDefense), $"Year {year}: no defense potion.");
            Assert.True(listings.Any(l => l.Item.Type == ItemType.Weapon), $"Year {year}: no weapon.");
            Assert.True(listings.Any(l => l.Item.Type == ItemType.Armor), $"Year {year}: no armour.");
        }
    }

    [Fact]
    public void ShippedWeaponCatalog_SpansTheFullRarityRange_FromCrudeToRelic()
    {
        var archetypes = ContentLoader.LoadItemArchetypes(Path.Combine(RealContentDirectory(), "item-archetypes.json"));
        var weaponRarities = archetypes
            .Where(a => a.Type == ItemType.Weapon)
            .Select(a => a.Rarity)
            .ToHashSet();

        // A minimal-damage weapon and a rare high-damage one must both exist.
        Assert.Contains(Rarity.Common, weaponRarities);
        Assert.Contains(Rarity.Legendary, weaponRarities);
        // ...and the bands in between, so gear progression has rungs.
        Assert.True(weaponRarities.Count >= 4, $"only {weaponRarities.Count} weapon rarity bands in the catalog");
    }

    [Fact]
    public void ShippedEquippables_HaveRarityConsistentWithTheirPower()
    {
        var archetypes = ContentLoader.LoadItemArchetypes(Path.Combine(RealContentDirectory(), "item-archetypes.json"));

        foreach (var a in archetypes.Where(a => a.IsEquippable))
        {
            Assert.Equal(RarityExtensions.ForPower(a.PowerMultiplier), a.Rarity);
        }
    }

    [Fact]
    public void EveryYearHasAGovernmentStoreCarryingAFullyLoadedRangedWeapon()
    {
        var world = ShippedWorld();

        foreach (var year in SampleYears)
        {
            var government = world.GetYear(year).StoreSlots
                .Where(s => s.Store is { IsGovernmentRun: true })
                .Select(s => s.Store!)
                .Single();

            var ranged = government.Listings.Select(l => l.Item).FirstOrDefault(i => i.IsRanged);
            Assert.True(ranged is not null, $"Year {year}: no ranged weapon in the government store.");
            Assert.True(ranged!.AmmoCapacity > 0, $"Year {year}: ranged weapon has no ammo capacity.");
            Assert.Equal(ranged.AmmoCapacity, ranged.AmmoRemaining);
        }
    }

    [Fact]
    public void SameSeed_RebuildsAnIdenticalYear()
    {
        var a = ContentLoader.LoadTimeWorld(RealContentDirectory(), 999).GetYear(3141);
        var b = ContentLoader.LoadTimeWorld(RealContentDirectory(), 999).GetYear(3141);

        Assert.Equal(Describe(a.Map), Describe(b.Map));
    }

    private static int ReachableRoomCount(Core.World.LevelMap map)
    {
        var visited = new HashSet<Core.World.Coordinate> { map.Start };
        var frontier = new Queue<Core.World.Coordinate>();
        frontier.Enqueue(map.Start);

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            foreach (var direction in map.GetRoom(current).ExitDescriptions.Keys)
            {
                var next = current.Move(direction);
                if (visited.Add(next))
                {
                    frontier.Enqueue(next);
                }
            }
        }

        return visited.Count;
    }

    private static string Describe(Core.World.LevelMap map) =>
        string.Join("|", map.Rooms
            .OrderBy(kv => kv.Key.North).ThenBy(kv => kv.Key.East)
            .Select(kv => $"{kv.Key}:{kv.Value.Description}"));
}
