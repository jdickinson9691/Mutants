using Mutants.Core.Characters;
using Mutants.Core.Classes;
using Mutants.Core.Ions;
using Mutants.Core.Levels;
using Mutants.Core.Monsters;
using Mutants.Core.World;
using Mutants.Engine.Simulation;

namespace Mutants.Engine.Tests.Simulation;

public class TimeTravelResolverTests
{
    // A fixed 0.5 roll keeps combat's damage-variance factor at exactly 1.0.
    private static StubRandomSource NeutralRandom() => StubRandomSource.Fixed(0.5);

    private static GameWorld ThreeLevelWorld() => new(
    [
        new WorldLevelDefinition(1, TestLevel.Build(), TestMonsters.RosterFor(1), []),
        new WorldLevelDefinition(2, GridLevelBuilder.Build("Level 2", Coordinate.Origin, new Dictionary<Coordinate, string> { [Coordinate.Origin] = "A hazy level 2." }),
            TestMonsters.RosterFor(2), [], gatekeeper: () => TestMonsters.Gatekeeper(2), minCharacterLevelToUnlock: 5),
        new WorldLevelDefinition(3, GridLevelBuilder.Build("Level 3", Coordinate.Origin, new Dictionary<Coordinate, string> { [Coordinate.Origin] = "A hazy level 3." }),
            TestMonsters.RosterFor(3), [], gatekeeper: () => TestMonsters.Gatekeeper(3), minCharacterLevelToUnlock: 10),
    ]);

    /// <summary>
    /// LevelUp() doesn't enforce the soft cap itself (see its doc
    /// comment), so this can reach any level directly. Also tops up Ions
    /// well past what the class formula would naturally give at this
    /// level: Ions come from converting loot (docs/GDD.md §2), a
    /// separate resource from XP/level, so a character meeting a time
    /// level's minimum character level has no guaranteed Ion balance —
    /// these tests are about TimeTravelResolver's sequencing, not
    /// grinding a realistic Ion stockpile.
    /// </summary>
    private static Mutant HighLevelMutant(int level)
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        for (var i = 1; i < level; i++)
        {
            mutant.LevelUp();
        }

        mutant.Ions.SetMax(500);
        mutant.Ions.Add(500);
        return mutant;
    }

    [Fact]
    public void Travel_ToAnUndefinedLevel_Fails()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        var result = TimeTravelResolver.Travel(mutant, ThreeLevelWorld(), targetLevel: 99, NeutralRandom());

        Assert.False(result.Success);
        Assert.Equal(TimeTravelFailureReason.UnknownLevel, result.FailureReason);
    }

    [Fact]
    public void Travel_ToCurrentLevelOne_SucceedsAsAFreeRetreat()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        var startingIons = mutant.Ions.Current;

        var result = TimeTravelResolver.Travel(mutant, ThreeLevelWorld(), targetLevel: 1, NeutralRandom());

        Assert.True(result.Success);
        Assert.Equal(1, result.NewLevel);
        Assert.Equal(startingIons, mutant.Ions.Current); // free
    }

    [Fact]
    public void Travel_Deeper_BelowMinimumCharacterLevel_Fails()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior); // level 1, needs level 5 for level 2
        var result = TimeTravelResolver.Travel(mutant, ThreeLevelWorld(), targetLevel: 2, NeutralRandom());

        Assert.False(result.Success);
        Assert.Equal(TimeTravelFailureReason.BelowMinimumCharacterLevel, result.FailureReason);
        Assert.Equal(1, mutant.UnlockedTimeLevel);
    }

    [Fact]
    public void Travel_Deeper_MeetsMinimumLevel_FightsAndBeatsGatekeeper_Unlocks()
    {
        var mutant = HighLevelMutant(5);
        var result = TimeTravelResolver.Travel(mutant, ThreeLevelWorld(), targetLevel: 2, NeutralRandom());

        Assert.True(result.Success);
        Assert.Equal(2, result.NewLevel);
        Assert.NotNull(result.GatekeeperFight);
        Assert.True(result.GatekeeperFight!.MutantWon);
        Assert.Equal(2, mutant.UnlockedTimeLevel);
        Assert.Equal(2, mutant.CurrentTimeLevel);
        Assert.True(mutant.HasDefeatedGatekeeper(2));
    }

    [Fact]
    public void Travel_Deeper_LosingToGatekeeper_FailsWithNoUnlockAndNoIonsSpent()
    {
        var mutant = HighLevelMutant(5);
        mutant.Health.Damage(mutant.Health.Max - 1); // 1 HP - the gatekeeper will win
        var startingIons = mutant.Ions.Current;

        var result = TimeTravelResolver.Travel(mutant, ThreeLevelWorld(), targetLevel: 2, NeutralRandom());

        Assert.False(result.Success);
        Assert.Equal(TimeTravelFailureReason.LostToGatekeeper, result.FailureReason);
        Assert.NotNull(result.GatekeeperFight);
        Assert.False(result.GatekeeperFight!.MutantWon);
        Assert.Equal(1, mutant.UnlockedTimeLevel);
        Assert.Equal(1, mutant.CurrentTimeLevel);
        Assert.False(mutant.HasDefeatedGatekeeper(2));
        Assert.Equal(startingIons, mutant.Ions.Current);
    }

    [Fact]
    public void Travel_Deeper_AlreadyDefeatedGatekeeper_SkipsTheRefight()
    {
        var mutant = HighLevelMutant(5);
        mutant.RecordGatekeeperDefeat(2);
        mutant.UnlockTimeLevel(2);

        var result = TimeTravelResolver.Travel(mutant, ThreeLevelWorld(), targetLevel: 2, NeutralRandom());

        Assert.True(result.Success);
        Assert.Null(result.GatekeeperFight); // no fight needed this time
    }

    [Fact]
    public void Travel_Deeper_InsufficientIons_FailsButKeepsAnyGatekeeperUnlockEarned()
    {
        var mutant = HighLevelMutant(5);
        mutant.Ions.Spend(mutant.Ions.Current); // 0 Ions - can't afford the 50-Ion trip to level 2

        var result = TimeTravelResolver.Travel(mutant, ThreeLevelWorld(), targetLevel: 2, NeutralRandom());

        Assert.False(result.Success);
        Assert.Equal(TimeTravelFailureReason.InsufficientIons, result.FailureReason);
        Assert.NotNull(result.GatekeeperFight);
        Assert.True(result.GatekeeperFight!.MutantWon);
        Assert.Equal(2, mutant.UnlockedTimeLevel); // unlock is permanent even though the jump itself failed
        Assert.True(mutant.HasDefeatedGatekeeper(2));
        Assert.Equal(1, mutant.CurrentTimeLevel); // never actually arrived
    }

    [Fact]
    public void Travel_ToAlreadyUnlockedDeeperLevel_ChargesIonsWithNoGatekeeperRefight()
    {
        var mutant = HighLevelMutant(5);
        mutant.RecordGatekeeperDefeat(2);
        mutant.UnlockTimeLevel(2);
        mutant.SetCurrentTimeLevel(1); // currently retreated back to level 1
        var startingIons = mutant.Ions.Current;

        var result = TimeTravelResolver.Travel(mutant, ThreeLevelWorld(), targetLevel: 2, NeutralRandom());

        Assert.True(result.Success);
        Assert.Null(result.GatekeeperFight);
        Assert.Equal(startingIons - IonEconomy.TimeTravelCost(2), mutant.Ions.Current);
    }

    [Fact]
    public void Travel_RetreatToAnUnlockedShallowerLevel_IsFree()
    {
        var mutant = HighLevelMutant(5);
        TimeTravelResolver.Travel(mutant, ThreeLevelWorld(), targetLevel: 2, NeutralRandom()); // now at level 2
        var ionsAtLevel2 = mutant.Ions.Current;

        var result = TimeTravelResolver.Travel(mutant, ThreeLevelWorld(), targetLevel: 1, NeutralRandom());

        Assert.True(result.Success);
        Assert.Equal(1, mutant.CurrentTimeLevel);
        Assert.Equal(ionsAtLevel2, mutant.Ions.Current); // retreat charged nothing
    }

    [Fact]
    public void Travel_PlacesTheMutantAtTheDestinationLevelsStartRoom()
    {
        var mutant = HighLevelMutant(5);
        var world = ThreeLevelWorld();

        TimeTravelResolver.Travel(mutant, world, targetLevel: 2, NeutralRandom());

        Assert.Equal(world.GetLevel(2).Map.Start, mutant.Position);
    }
}
