using Mutants.Core.Time;
using Mutants.Core.World;

namespace Mutants.Core.Tests.Time;

public class YearMapFactoryTests
{
    private static readonly EraDefinition SampleEra = new(
        2000, "Sample Era",
        ["a cracked plaza.", "a silent corridor.", "a collapsed stairwell.", "an open rooftop."],
        ["some-species"], []);

    [Theory]
    [InlineData(2000)]
    [InlineData(2137)]
    [InlineData(3050)]
    [InlineData(4999)]
    [InlineData(5000)]
    public void Build_ProducesAWellFormedFullyConnectedGrid(int year)
    {
        var map = YearMapFactory.Build(worldSeed: 314159, SampleEra, year);

        Assert.True(map.RoomCount >= 9);
        Assert.True(map.Rooms.ContainsKey(Coordinate.Origin));
        MapAssert.IsWellFormedAndFullyConnected(map);
    }

    [Fact]
    public void Build_IsDeterministicForTheSameSeedAndYear()
    {
        var a = YearMapFactory.Build(worldSeed: 7, SampleEra, year: 2500);
        var b = YearMapFactory.Build(worldSeed: 7, SampleEra, year: 2500);

        Assert.Equal(a.RoomCount, b.RoomCount);
        Assert.Equal(Describe(a), Describe(b));
    }

    [Fact]
    public void Build_DiffersAcrossYearsAndAcrossSeeds()
    {
        var baseline = Describe(YearMapFactory.Build(worldSeed: 7, SampleEra, year: 2500));
        var otherYear = Describe(YearMapFactory.Build(worldSeed: 7, SampleEra, year: 2501));
        var otherSeed = Describe(YearMapFactory.Build(worldSeed: 8, SampleEra, year: 2500));

        Assert.NotEqual(baseline, otherYear);
        Assert.NotEqual(baseline, otherSeed);
    }

    [Fact]
    public void Build_OnlyUsesRoomTextFromTheErasPool()
    {
        var map = YearMapFactory.Build(worldSeed: 1, SampleEra, year: 2000);
        var pool = new HashSet<string>(SampleEra.RoomText);

        Assert.All(map.Rooms.Values, room => Assert.Contains(room.Description, pool));
    }

    [Fact]
    public void Build_NamesTheMapAfterTheEraAndYear()
    {
        var map = YearMapFactory.Build(worldSeed: 1, SampleEra, year: 3333);
        Assert.Equal("Sample Era — 3333 A.D.", map.Name);
    }

    private static string Describe(LevelMap map) =>
        string.Join("|", map.Rooms
            .OrderBy(kv => kv.Key.North).ThenBy(kv => kv.Key.East)
            .Select(kv => $"{kv.Key}:{kv.Value.Description}"));
}
