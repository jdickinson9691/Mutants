using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Tachyons;
using ChronoTravelers.Core.Time;
using ChronoTravelers.Engine.Simulation;

namespace ChronoTravelers.Engine.Tests.Simulation;

public class TimeTravelResolverTests
{
    private static StubRandomSource NeutralRandom() => StubRandomSource.Fixed(0.5);

    private static TimeWorld World() => TestTimeWorld.Build(seed: 4242);

    private static Traveler RichTraveler(int startingYear = 2000)
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier, startingYear);
        traveler.Tachyons.SetMax(2000);
        traveler.Tachyons.Add(2000);
        return traveler;
    }

    [Fact]
    public void Travel_ToAYearOffTheTimeline_Fails()
    {
        var traveler = RichTraveler();
        var result = TimeTravelResolver.Travel(traveler, World(), targetYear: 9999, NeutralRandom());

        Assert.False(result.Success);
        Assert.Equal(TimeTravelFailureReason.YearOutOfRange, result.FailureReason);
        Assert.Equal(2000, traveler.CurrentYear);
    }

    [Fact]
    public void Travel_ChargesCeilOfTheCoefficientTimesTheDistance_Symmetrically()
    {
        var traveler = RichTraveler(2000);

        var forward = TimeTravelResolver.Travel(traveler, World(), targetYear: 2500, NeutralRandom());
        Assert.True(forward.Success);
        Assert.Equal(20, forward.TachyonsSpent); // ceil(0.04 * 500)

        var back = TimeTravelResolver.Travel(traveler, World(), targetYear: 2000, NeutralRandom());
        Assert.True(back.Success);
        Assert.Equal(20, back.TachyonsSpent); // retreat costs the same
    }

    [Fact]
    public void Travel_WithoutEnoughTachyons_FailsAndChangesNothing()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        traveler.Tachyons.Spend(traveler.Tachyons.Current); // 0 Tachyons

        var result = TimeTravelResolver.Travel(traveler, World(), targetYear: 3000, NeutralRandom());

        Assert.False(result.Success);
        Assert.Equal(TimeTravelFailureReason.InsufficientTachyons, result.FailureReason);
        Assert.Equal(2000, traveler.CurrentYear);
    }

    [Fact]
    public void Travel_Success_MovesTheYear_SpendsTachyons_AndAdvancesFurthestYearReached()
    {
        var traveler = RichTraveler(2000);
        var before = traveler.Tachyons.Current;

        var result = TimeTravelResolver.Travel(traveler, World(), targetYear: 4200, NeutralRandom());

        Assert.True(result.Success);
        Assert.Equal(4200, result.NewYear);
        Assert.Equal(4200, traveler.CurrentYear);
        Assert.Equal(4200, traveler.FurthestYearReached);
        Assert.Equal(before - result.TachyonsSpent, traveler.Tachyons.Current);
    }

    [Fact]
    public void Travel_Retreat_MovesCurrentYearButNotFurthestYearReached()
    {
        var traveler = RichTraveler(2000);
        TimeTravelResolver.Travel(traveler, World(), targetYear: 4000, NeutralRandom());

        TimeTravelResolver.Travel(traveler, World(), targetYear: 2300, NeutralRandom());

        Assert.Equal(2300, traveler.CurrentYear);
        Assert.Equal(4000, traveler.FurthestYearReached);
    }

    [Fact]
    public void Travel_PlacesTheTravelerAtTheDestinationYearsStartRoom()
    {
        var traveler = RichTraveler(2000);
        var world = World();

        TimeTravelResolver.Travel(traveler, world, targetYear: 3300, NeutralRandom());

        Assert.Equal(world.GetYear(3300).Map.Start, traveler.Position);
    }

    [Fact]
    public void Travel_NeverFightsAWarden_EvenIntoAWardenYear()
    {
        var world = World();
        var wardenYear = world.WardenYears.First();
        var traveler = RichTraveler(2000);
        var hpBefore = traveler.Health.Current;

        var result = TimeTravelResolver.Travel(traveler, world, wardenYear, NeutralRandom());

        Assert.True(result.Success);
        Assert.Equal(hpBefore, traveler.Health.Current); // no fight happened
        Assert.False(traveler.HasDefeatedWarden(wardenYear)); // still there to fight in-year
    }

    [Fact]
    public void Travel_ToTheSameYear_IsAFreeNoOp()
    {
        var traveler = RichTraveler(2500);
        var before = traveler.Tachyons.Current;

        var result = TimeTravelResolver.Travel(traveler, World(), targetYear: 2500, NeutralRandom());

        Assert.True(result.Success);
        Assert.Equal(0, result.TachyonsSpent);
        Assert.Equal(before, traveler.Tachyons.Current);
    }

    [Fact]
    public void TimeTravelCostConstant_IsWiredThroughTachyonEconomy()
    {
        Assert.Equal(20, TachyonEconomy.TimeTravelCost(2000, 2500));
    }
}
