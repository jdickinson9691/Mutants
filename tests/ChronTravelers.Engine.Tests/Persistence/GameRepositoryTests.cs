using ChronTravelers.Engine.Persistence;

namespace ChronTravelers.Engine.Tests.Persistence;

public class GameRepositoryTests
{
    private static CharacterSaveData SampleCharacter(string name = "Rook") => new()
    {
        Name = name,
        Class = "Soldier",
        Level = 3,
        Xp = 250,
        Strength = 17,
        Agility = 10,
        Resolve = 8,
        Intellect = 8,
        CurrentHp = 30,
        MaxHp = 42,
        CurrentIons = 20,
        MaxIons = 24,
        Credits = 15,
        UnlockedTimeLevel = 1,
        CurrentTimeLevel = 1,
        PositionEast = 0,
        PositionNorth = 0,
    };

    [Fact]
    public void SaveAndLoadCharacter_RoundTrips()
    {
        using var repository = GameRepository.InMemory();
        repository.SaveCharacter(SampleCharacter());

        var loaded = repository.LoadCharacter("Rook");

        Assert.NotNull(loaded);
        Assert.Equal(3, loaded!.Level);
        Assert.Equal(250, loaded.Xp);
    }

    [Fact]
    public void LoadCharacter_ReturnsNullWhenNoSaveExists()
    {
        using var repository = GameRepository.InMemory();
        Assert.Null(repository.LoadCharacter("Nobody"));
    }

    [Fact]
    public void SaveCharacter_OverwritesAnExistingSaveWithTheSameName()
    {
        using var repository = GameRepository.InMemory();
        repository.SaveCharacter(SampleCharacter());

        var updated = SampleCharacter();
        updated.Level = 9;
        repository.SaveCharacter(updated);

        var characters = repository.ListSavedCharacterNames();
        Assert.Single(characters);
        Assert.Equal(9, repository.LoadCharacter("Rook")!.Level);
    }

    [Fact]
    public void ListSavedCharacterNames_ReturnsAllSavedNamesSorted()
    {
        using var repository = GameRepository.InMemory();
        repository.SaveCharacter(SampleCharacter("Zeta"));
        repository.SaveCharacter(SampleCharacter("Ashen"));

        var names = repository.ListSavedCharacterNames();

        Assert.Equal(["Ashen", "Zeta"], names);
    }

    [Fact]
    public void RecordPersonalBests_CreatesANewEntry()
    {
        using var repository = GameRepository.InMemory();
        repository.RecordPersonalBests("Rook", isPlayer: true, furthestYearReached: 2400, highestCharacterLevel: 5);

        var entry = repository.GetLeaderboardEntry("Rook");

        Assert.NotNull(entry);
        Assert.True(entry!.IsPlayer);
        Assert.Equal(2400, entry.FurthestYearReached);
        Assert.Equal(5, entry.HighestCharacterLevelReached);
    }

    [Fact]
    public void RecordPersonalBests_NeverLowersAnExistingBest()
    {
        using var repository = GameRepository.InMemory();
        repository.RecordPersonalBests("Rook", true, furthestYearReached: 3400, highestCharacterLevel: 10);

        repository.RecordPersonalBests("Rook", true, furthestYearReached: 2100, highestCharacterLevel: 1);

        var entry = repository.GetLeaderboardEntry("Rook");
        Assert.Equal(3400, entry!.FurthestYearReached);
        Assert.Equal(10, entry.HighestCharacterLevelReached);
    }

    [Fact]
    public void RecordPersonalBests_RaisesAnExistingBestWhenBeaten()
    {
        using var repository = GameRepository.InMemory();
        repository.RecordPersonalBests("Rook", true, furthestYearReached: 2100, highestCharacterLevel: 1);

        repository.RecordPersonalBests("Rook", true, furthestYearReached: 4400, highestCharacterLevel: 12);

        var entry = repository.GetLeaderboardEntry("Rook");
        Assert.Equal(4400, entry!.FurthestYearReached);
        Assert.Equal(12, entry.HighestCharacterLevelReached);
    }

    [Fact]
    public void TopByFurthestYear_OrdersDescending()
    {
        using var repository = GameRepository.InMemory();
        repository.RecordPersonalBests("Low", false, 2100, 1);
        repository.RecordPersonalBests("High", false, 4800, 3);
        repository.RecordPersonalBests("Mid", false, 3200, 2);

        var top = repository.TopByFurthestYear(10);

        Assert.Equal(["High", "Mid", "Low"], top.Select(e => e.Name));
    }

    [Fact]
    public void TopByCharacterLevel_OrdersDescending()
    {
        using var repository = GameRepository.InMemory();
        repository.RecordPersonalBests("Low", false, 2100, 1);
        repository.RecordPersonalBests("High", false, 2100, 20);
        repository.RecordPersonalBests("Mid", false, 2100, 10);

        var top = repository.TopByCharacterLevel(10);

        Assert.Equal(["High", "Mid", "Low"], top.Select(e => e.Name));
    }

    [Fact]
    public void TopByFurthestYear_RespectsTheRequestedCount()
    {
        using var repository = GameRepository.InMemory();
        for (var i = 1; i <= 15; i++)
        {
            repository.RecordPersonalBests($"Npc{i}", false, 2000 + i * 100, i);
        }

        Assert.Equal(10, repository.TopByFurthestYear(10).Count);
    }

    [Fact]
    public void GetLeaderboardEntry_ReturnsNullWhenAbsent()
    {
        using var repository = GameRepository.InMemory();
        Assert.Null(repository.GetLeaderboardEntry("Nobody"));
    }
}
