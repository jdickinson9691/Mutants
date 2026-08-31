using Mutants.Core.Characters;
using Mutants.Core.Classes;
using Mutants.Core.Ions;
using Mutants.Core.Time;
using Mutants.Engine.Simulation;

namespace Mutants.Engine.Tests.Simulation;

public class TimeTravelResolverTests
{
    private static StubRandomSource NeutralRandom() => StubRandomSource.Fixed(0.5);

    private static TimeWorld World() => TestTimeWorld.Build(seed: 4242);

    private static Mutant RichMutant(int startingYear = 2000)
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior, startingYear);
        mutant.Ions.SetMax(2000);
        mutant.Ions.Add(2000);
        return mutant;
    }

    [Fact]
    public void Travel_ToAYearOffTheTimeline_Fails()
    {
        var mutant = RichMutant();
        var result = TimeTravelResolver.Travel(mutant, World(), targetYear: 9999, NeutralRandom());

        Assert.False(result.Success);
        Assert.Equal(TimeTravelFailureReason.YearOutOfRange, result.FailureReason);
        Assert.Equal(2000, mutant.CurrentYear);
    }

    [Fact]
    public void Travel_ChargesCeilOfTheCoefficientTimesTheDistance_Symmetrically()
    {
        var mutant = RichMutant(2000);

        var forward = TimeTravelResolver.Travel(mutant, World(), targetYear: 2500, NeutralRandom());
        Assert.True(forward.Success);
        Assert.Equal(50, forward.IonsSpent); // ceil(0.1 * 500)

        var back = TimeTravelResolver.Travel(mutant, World(), targetYear: 2000, NeutralRandom());
        Assert.True(back.Success);
        Assert.Equal(50, back.IonsSpent); // retreat costs the same
    }

    [Fact]
    public void Travel_WithoutEnoughIons_FailsAndChangesNothing()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        mutant.Ions.Spend(mutant.Ions.Current); // 0 Ions

        var result = TimeTravelResolver.Travel(mutant, World(), targetYear: 3000, NeutralRandom());

        Assert.False(result.Success);
        Assert.Equal(TimeTravelFailureReason.InsufficientIons, result.FailureReason);
        Assert.Equal(2000, mutant.CurrentYear);
    }

    [Fact]
    public void Travel_Success_MovesTheYear_SpendsIons_AndAdvancesFurthestYearReached()
    {
        var mutant = RichMutant(2000);
        var before = mutant.Ions.Current;

        var result = TimeTravelResolver.Travel(mutant, World(), targetYear: 4200, NeutralRandom());

        Assert.True(result.Success);
        Assert.Equal(4200, result.NewYear);
        Assert.Equal(4200, mutant.CurrentYear);
        Assert.Equal(4200, mutant.FurthestYearReached);
        Assert.Equal(before - result.IonsSpent, mutant.Ions.Current);
    }

    [Fact]
    public void Travel_Retreat_MovesCurrentYearButNotFurthestYearReached()
    {
        var mutant = RichMutant(2000);
        TimeTravelResolver.Travel(mutant, World(), targetYear: 4000, NeutralRandom());

        TimeTravelResolver.Travel(mutant, World(), targetYear: 2300, NeutralRandom());

        Assert.Equal(2300, mutant.CurrentYear);
        Assert.Equal(4000, mutant.FurthestYearReached);
    }

    [Fact]
    public void Travel_PlacesTheMutantAtTheDestinationYearsStartRoom()
    {
        var mutant = RichMutant(2000);
        var world = World();

        TimeTravelResolver.Travel(mutant, world, targetYear: 3300, NeutralRandom());

        Assert.Equal(world.GetYear(3300).Map.Start, mutant.Position);
    }

    [Fact]
    public void Travel_NeverFightsAGatekeeper_EvenIntoAGatekeeperYear()
    {
        var world = World();
        var gatekeeperYear = world.GatekeeperYears.First();
        var mutant = RichMutant(2000);
        var hpBefore = mutant.Health.Current;

        var result = TimeTravelResolver.Travel(mutant, world, gatekeeperYear, NeutralRandom());

        Assert.True(result.Success);
        Assert.Equal(hpBefore, mutant.Health.Current); // no fight happened
        Assert.False(mutant.HasDefeatedGatekeeper(gatekeeperYear)); // still there to fight in-year
    }

    [Fact]
    public void Travel_ToTheSameYear_IsAFreeNoOp()
    {
        var mutant = RichMutant(2500);
        var before = mutant.Ions.Current;

        var result = TimeTravelResolver.Travel(mutant, World(), targetYear: 2500, NeutralRandom());

        Assert.True(result.Success);
        Assert.Equal(0, result.IonsSpent);
        Assert.Equal(before, mutant.Ions.Current);
    }

    [Fact]
    public void TimeTravelCostConstant_IsWiredThroughIonEconomy()
    {
        Assert.Equal(50, IonEconomy.TimeTravelCost(2000, 2500));
    }
}
