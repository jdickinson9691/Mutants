using ChronoTravelers.Core.Time;

namespace ChronoTravelers.Core.Tests.Time;

public class EraTableTests
{
    private static EraDefinition Era(int fromYear, string name = "Era") =>
        new(fromYear, name, ["a room."], ["some-species"], []);

    [Fact]
    public void EraForYear_ReturnsTheLastEraStartingOnOrBeforeTheYear()
    {
        var table = new EraTable([Era(2000, "First"), Era(2500, "Second"), Era(4000, "Third")]);

        Assert.Equal("First", table.EraForYear(2000).Name);
        Assert.Equal("First", table.EraForYear(2499).Name);
        Assert.Equal("Second", table.EraForYear(2500).Name);
        Assert.Equal("Second", table.EraForYear(3999).Name);
        Assert.Equal("Third", table.EraForYear(4000).Name);
        Assert.Equal("Third", table.EraForYear(5000).Name);
    }

    [Fact]
    public void Constructor_SortsErasByFromYear()
    {
        var table = new EraTable([Era(4000, "Third"), Era(2000, "First"), Era(2500, "Second")]);

        Assert.Equal(new[] { "First", "Second", "Third" }, table.Eras.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void Constructor_RejectsAFirstEraThatDoesNotStartAtYear2000()
    {
        Assert.Throws<ArgumentException>(() => new EraTable([Era(2100)]));
    }

    [Fact]
    public void Constructor_RejectsTwoErasSharingAFromYear()
    {
        Assert.Throws<ArgumentException>(() => new EraTable([Era(2000, "A"), Era(2000, "B")]));
    }

    [Fact]
    public void Constructor_RejectsAnEmptyTable()
    {
        Assert.Throws<ArgumentException>(() => new EraTable([]));
    }

    [Fact]
    public void Constructor_RejectsAnEraWithNoRoomTextOrNoSpecies()
    {
        Assert.Throws<ArgumentException>(() =>
            new EraTable([new EraDefinition(2000, "Empty rooms", [], ["s"], [])]));
        Assert.Throws<ArgumentException>(() =>
            new EraTable([new EraDefinition(2000, "Empty species", ["r."], [], [])]));
    }

    [Fact]
    public void EraForYear_RejectsYearsOutsideTheTimeline()
    {
        var table = new EraTable([Era(2000)]);
        Assert.Throws<ArgumentOutOfRangeException>(() => table.EraForYear(1999));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.EraForYear(5001));
    }
}
