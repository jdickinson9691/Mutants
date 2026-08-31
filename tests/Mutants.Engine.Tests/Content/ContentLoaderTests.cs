using Mutants.Engine.Content;

namespace Mutants.Engine.Tests.Content;

public class ContentLoaderTests
{
    [Fact]
    public void LoadAbilities_ParsesEntries()
    {
        using var dir = new TempContentDirectory();
        var path = dir.WriteFile("abilities.json", """
            [{ "class": "Warrior", "tier": 1, "level": 5, "name": "Cleave", "description": "Hit up to 2 additional adjacent enemies." }]
            """);

        var abilities = ContentLoader.LoadAbilities(path);

        Assert.Single(abilities);
        Assert.Equal("Cleave", abilities[0].Name);
        Assert.Equal(5, abilities[0].Level);
    }

    [Fact]
    public void LoadNpcCount_ParsesTheTotal()
    {
        using var dir = new TempContentDirectory();
        var path = dir.WriteFile("npc-population.json", """{ "totalCount": 17 }""");

        Assert.Equal(17, ContentLoader.LoadNpcCount(path));
    }

    [Fact]
    public void LoadNpcCount_ClampsNegativeToZero()
    {
        using var dir = new TempContentDirectory();
        var path = dir.WriteFile("npc-population.json", """{ "totalCount": -5 }""");

        Assert.Equal(0, ContentLoader.LoadNpcCount(path));
    }

    // --- LoadTimeWorld (continuous timeline) ---------------------------------

    private const string MinimalSpeciesJson = """
        [ { "id": "grunt", "name": "Grunt", "archetype": "Baseline", "lootThemeTags": ["common"] } ]
        """;

    private const string MinimalArchetypesJson = """
        [
          { "id": "w", "name": "Blade", "type": "Weapon", "rarity": "Common", "themeTags": ["common"] },
          { "id": "a", "name": "Plate", "type": "Armor", "rarity": "Common", "themeTags": ["common"] },
          { "id": "h", "name": "Ration", "type": "Consumable", "rarity": "Common", "effect": "Heal", "effectMagnitude": 10, "themeTags": ["common"] },
          { "id": "ba", "name": "Stim", "type": "Consumable", "rarity": "Common", "effect": "BuffAttack", "effectMagnitude": 3, "effectDurationTicks": 15, "themeTags": ["common"] },
          { "id": "bd", "name": "Ward", "type": "Consumable", "rarity": "Common", "effect": "BuffDefense", "effectMagnitude": 3, "effectDurationTicks": 15, "themeTags": ["common"] }
        ]
        """;

    private const string MinimalErasJson = """
        [ { "fromYear": 2000, "name": "Start", "roomText": ["a room."], "speciesIds": ["grunt"], "itemThemeTags": ["common"] } ]
        """;

    private static string WriteMinimalTimeline(TempContentDirectory dir, string? species = null, string? archetypes = null, string? eras = null)
    {
        dir.WriteFile("monster-species.json", species ?? MinimalSpeciesJson);
        dir.WriteFile("item-archetypes.json", archetypes ?? MinimalArchetypesJson);
        dir.WriteFile("eras.json", eras ?? MinimalErasJson);
        return dir.Path;
    }

    [Fact]
    public void LoadTimeWorld_BuildsAWorkingWorldFromMinimalCatalogs()
    {
        using var dir = new TempContentDirectory();
        var world = ContentLoader.LoadTimeWorld(WriteMinimalTimeline(dir), worldSeed: 1);

        var content = world.GetYear(2500);
        Assert.NotEmpty(content.MonsterRoster);
        Assert.Equal("Start", content.Era.Name);
    }

    [Fact]
    public void LoadTimeWorld_ThrowsOnUnknownArchetypeEnum()
    {
        using var dir = new TempContentDirectory();
        var badSpecies = """[ { "id": "x", "name": "X", "archetype": "Nonsense", "lootThemeTags": ["common"] } ]""";

        Assert.Throws<ContentException>(() =>
            ContentLoader.LoadTimeWorld(WriteMinimalTimeline(dir, species: badSpecies), worldSeed: 1));
    }

    [Fact]
    public void LoadTimeWorld_ThrowsWhenTheFirstEraDoesNotStartAt2000()
    {
        using var dir = new TempContentDirectory();
        var badEras = """[ { "fromYear": 2100, "name": "Late", "roomText": ["r."], "speciesIds": ["grunt"], "itemThemeTags": ["common"] } ]""";

        Assert.Throws<ContentException>(() =>
            ContentLoader.LoadTimeWorld(WriteMinimalTimeline(dir, eras: badEras), worldSeed: 1));
    }

    [Fact]
    public void LoadTimeWorld_ThrowsWhenAnEraReferencesAnUnknownSpecies()
    {
        using var dir = new TempContentDirectory();
        var badEras = """[ { "fromYear": 2000, "name": "S", "roomText": ["r."], "speciesIds": ["ghost"], "itemThemeTags": ["common"] } ]""";

        Assert.Throws<ContentException>(() =>
            ContentLoader.LoadTimeWorld(WriteMinimalTimeline(dir, eras: badEras), worldSeed: 1));
    }

    [Fact]
    public void LoadTimeWorld_ThrowsWhenTheCatalogCannotStockAStaple()
    {
        using var dir = new TempContentDirectory();
        var noDefensePotion = """
            [
              { "id": "w", "name": "Blade", "type": "Weapon", "rarity": "Common", "themeTags": ["common"] },
              { "id": "a", "name": "Plate", "type": "Armor", "rarity": "Common", "themeTags": ["common"] },
              { "id": "h", "name": "Ration", "type": "Consumable", "rarity": "Common", "effect": "Heal", "effectMagnitude": 10, "themeTags": ["common"] },
              { "id": "ba", "name": "Stim", "type": "Consumable", "rarity": "Common", "effect": "BuffAttack", "effectMagnitude": 3, "effectDurationTicks": 15, "themeTags": ["common"] }
            ]
            """;

        Assert.Throws<ContentException>(() =>
            ContentLoader.LoadTimeWorld(WriteMinimalTimeline(dir, archetypes: noDefensePotion), worldSeed: 1));
    }

    [Fact]
    public void LoadTimeWorld_ThrowsForAMissingCatalogFile()
    {
        using var dir = new TempContentDirectory();
        dir.WriteFile("monster-species.json", MinimalSpeciesJson);
        // no item-archetypes.json / eras.json
        Assert.Throws<ContentException>(() => ContentLoader.LoadTimeWorld(dir.Path, worldSeed: 1));
    }

    [Fact]
    public void LoadTimeWorld_ThrowsForInvalidJson()
    {
        using var dir = new TempContentDirectory();
        dir.WriteFile("monster-species.json", "{ not valid ]");
        dir.WriteFile("item-archetypes.json", MinimalArchetypesJson);
        dir.WriteFile("eras.json", MinimalErasJson);

        Assert.Throws<ContentException>(() => ContentLoader.LoadTimeWorld(dir.Path, worldSeed: 1));
    }
}
