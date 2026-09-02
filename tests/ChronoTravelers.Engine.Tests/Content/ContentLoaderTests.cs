using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Engine.Content;

namespace ChronoTravelers.Engine.Tests.Content;

public class ContentLoaderTests
{
    [Fact]
    public void LoadAbilities_ParsesEntries()
    {
        using var dir = new TempContentDirectory();
        var path = dir.WriteFile("abilities.json", """
            [{ "class": "Soldier", "tier": 1, "level": 5, "name": "Cleave", "description": "Hit up to 2 additional adjacent enemies." }]
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

    [Fact]
    public void LoadNpcClassWeights_ReturnsNullWhenTheFieldIsAbsent()
    {
        using var dir = new TempContentDirectory();
        var path = dir.WriteFile("npc-population.json", """{ "totalCount": 5 }""");

        Assert.Null(ContentLoader.LoadNpcClassWeights(path));
    }

    [Fact]
    public void LoadNpcClassWeights_ReturnsNullWhenTheMapIsEmpty()
    {
        using var dir = new TempContentDirectory();
        var path = dir.WriteFile("npc-population.json", """{ "totalCount": 5, "classWeights": {} }""");

        Assert.Null(ContentLoader.LoadNpcClassWeights(path));
    }

    [Fact]
    public void LoadNpcClassWeights_ParsesEachClassNameCaseInsensitively()
    {
        using var dir = new TempContentDirectory();
        var path = dir.WriteFile("npc-population.json", """
            { "totalCount": 5, "classWeights": { "soldier": 2, "DOCTOR": 1 } }
            """);

        var weights = ContentLoader.LoadNpcClassWeights(path);

        Assert.NotNull(weights);
        Assert.Equal(2, weights!.Count);
        Assert.Equal(2, weights[CharacterClass.Soldier]);
        Assert.Equal(1, weights[CharacterClass.Doctor]);
    }

    [Fact]
    public void LoadNpcClassWeights_ThrowsOnAnUnknownClassName()
    {
        using var dir = new TempContentDirectory();
        var path = dir.WriteFile("npc-population.json", """
            { "totalCount": 5, "classWeights": { "Wizard": 1 } }
            """);

        Assert.Throws<ContentException>(() => ContentLoader.LoadNpcClassWeights(path));
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
    public void LoadTimeWorld_ParsesARangedArchetype_AndStocksItWithAFullMagazine()
    {
        using var dir = new TempContentDirectory();
        var withWand = MinimalArchetypesJson.TrimEnd().TrimEnd(']')
            + """
            ,
              { "id": "wand", "name": "Test Wand", "type": "Ranged", "rarity": "Uncommon", "rangedKind": "Wand", "ammoCapacity": 5, "rangedEffect": "Weaken", "effectMagnitude": 2, "themeTags": ["common"] }
            ]
            """;

        var world = ContentLoader.LoadTimeWorld(WriteMinimalTimeline(dir, archetypes: withWand), worldSeed: 1);

        var government = world.GetYear(2400).StoreSlots
            .Select(s => s.Store)
            .Single(s => s is { IsGovernmentRun: true })!;

        var ranged = government.Listings.Select(l => l.Item).Single(i => i.IsRanged);
        Assert.Equal(RangedKind.Wand, ranged.RangedKind);
        Assert.Equal(RangedEffectType.Weaken, ranged.RangedEffect);
        Assert.Equal(5, ranged.AmmoCapacity);
        Assert.Equal(5, ranged.AmmoRemaining);
        Assert.NotEqual(Guid.Empty, ranged.InstanceId);
    }

    [Fact]
    public void LoadTimeWorld_ParsesAStaggerRangedArchetype()
    {
        using var dir = new TempContentDirectory();
        var withTaser = MinimalArchetypesJson.TrimEnd().TrimEnd(']')
            + """
            ,
              { "id": "taser", "name": "Test Taser", "type": "Ranged", "rarity": "Uncommon", "rangedKind": "Gun", "ammoCapacity": 5, "rangedEffect": "Stagger", "effectMagnitude": 2, "themeTags": ["common"] }
            ]
            """;

        var world = ContentLoader.LoadTimeWorld(WriteMinimalTimeline(dir, archetypes: withTaser), worldSeed: 1);

        var government = world.GetYear(2400).StoreSlots
            .Select(s => s.Store)
            .Single(s => s is { IsGovernmentRun: true })!;

        var ranged = government.Listings.Select(l => l.Item).First(i => i.IsRanged && i.RangedEffect == RangedEffectType.Stagger);
        Assert.Equal(RangedKind.Gun, ranged.RangedKind);
        Assert.Equal(RangedEffectType.Stagger, ranged.RangedEffect);
    }

    [Fact]
    public void LoadItemArchetypes_ParsesTheNewConsumableEffectTypes()
    {
        using var dir = new TempContentDirectory();
        var path = dir.WriteFile("item-archetypes.json", """
            [
              { "id": "cell", "name": "Battery Cell", "type": "Consumable", "rarity": "Common", "effect": "RestoreTachyons", "effectMagnitude": 12, "themeTags": ["common"] },
              { "id": "regen", "name": "Regen Tonic", "type": "Consumable", "rarity": "Common", "effect": "HealOverTime", "effectMagnitude": 4, "effectDurationTicks": 10, "themeTags": ["common"] },
              { "id": "quick", "name": "Quickstep Draught", "type": "Consumable", "rarity": "Common", "effect": "BuffSpeed", "effectMagnitude": 3, "effectDurationTicks": 10, "themeTags": ["common"] }
            ]
            """);

        var archetypes = ContentLoader.LoadItemArchetypes(path);

        Assert.Equal(ConsumableEffectType.RestoreTachyons, archetypes.Single(a => a.Id == "cell").Effect);
        Assert.Equal(ConsumableEffectType.HealOverTime, archetypes.Single(a => a.Id == "regen").Effect);
        Assert.Equal(ConsumableEffectType.BuffSpeed, archetypes.Single(a => a.Id == "quick").Effect);
    }

    [Fact]
    public void LoadTimeWorld_ThrowsOnUnknownRangedKind()
    {
        using var dir = new TempContentDirectory();
        var badRanged = MinimalArchetypesJson.TrimEnd().TrimEnd(']')
            + """
            ,
              { "id": "boom", "name": "Bazooka", "type": "Ranged", "rangedKind": "Bazooka", "ammoCapacity": 3, "themeTags": ["common"] }
            ]
            """;

        Assert.Throws<ContentException>(() =>
            ContentLoader.LoadTimeWorld(WriteMinimalTimeline(dir, archetypes: badRanged), worldSeed: 1));
    }

    [Fact]
    public void LoadTimeWorld_ThrowsWhenRangedKindIsSetButTypeIsNotRanged()
    {
        using var dir = new TempContentDirectory();
        var mismatched = MinimalArchetypesJson.TrimEnd().TrimEnd(']')
            + """
            ,
              { "id": "oops", "name": "Confused Bow", "type": "Weapon", "rangedKind": "Bow", "ammoCapacity": 8, "themeTags": ["common"] }
            ]
            """;

        Assert.Throws<ContentException>(() =>
            ContentLoader.LoadTimeWorld(WriteMinimalTimeline(dir, archetypes: mismatched), worldSeed: 1));
    }

    [Fact]
    public void LoadTimeWorld_DerivesEquippableRarityFromPowerMultiplier_IgnoringAnyAuthoredRarity()
    {
        using var dir = new TempContentDirectory();
        // The weapon claims "Common" but its power puts it in the Legendary band.
        var archetypes = """
            [
              { "id": "w", "name": "Relic Blade", "type": "Weapon", "rarity": "Common", "powerMultiplier": 2.9, "themeTags": ["common"] },
              { "id": "a", "name": "Plate", "type": "Armor", "powerMultiplier": 1.0, "themeTags": ["common"] },
              { "id": "h", "name": "Ration", "type": "Consumable", "rarity": "Common", "effect": "Heal", "effectMagnitude": 10, "themeTags": ["common"] },
              { "id": "ba", "name": "Stim", "type": "Consumable", "rarity": "Common", "effect": "BuffAttack", "effectMagnitude": 3, "effectDurationTicks": 15, "themeTags": ["common"] },
              { "id": "bd", "name": "Ward", "type": "Consumable", "rarity": "Common", "effect": "BuffDefense", "effectMagnitude": 3, "effectDurationTicks": 15, "themeTags": ["common"] }
            ]
            """;

        var world = ContentLoader.LoadTimeWorld(WriteMinimalTimeline(dir, archetypes: archetypes), worldSeed: 1);
        var government = world.GetYear(2400).StoreSlots.Select(s => s.Store).Single(s => s is { IsGovernmentRun: true })!;
        var blade = government.Listings.Select(l => l.Item).Single(i => i.Type == ItemType.Weapon);

        Assert.Equal(Rarity.Legendary, blade.Rarity);
        Assert.True(blade.AttackBonus > 0);
    }

    [Fact]
    public void LoadTimeWorld_ThrowsWhenAnEquippablePowerMultiplierIsOutOfRange()
    {
        using var dir = new TempContentDirectory();
        var archetypes = MinimalArchetypesJson.TrimEnd().TrimEnd(']')
            + """
            ,
              { "id": "broken", "name": "Broken Blade", "type": "Weapon", "powerMultiplier": 9.0, "themeTags": ["common"] }
            ]
            """;

        Assert.Throws<ContentException>(() =>
            ContentLoader.LoadTimeWorld(WriteMinimalTimeline(dir, archetypes: archetypes), worldSeed: 1));
    }

    [Fact]
    public void LoadTimeWorld_MissingStoreTemplateFile_FallsBackToDefaults()
    {
        using var dir = new TempContentDirectory();
        // No store-templates.json written — it's optional.
        var world = ContentLoader.LoadTimeWorld(WriteMinimalTimeline(dir), worldSeed: 1);

        var vacant = world.GetYear(2500).StoreSlots.Count(s => s.IsAvailableForPurchase);

        Assert.Equal(3, vacant); // StoreTemplateData's default PlayerSlotCount
    }

    [Fact]
    public void LoadTimeWorld_ReadsPlayerSlotCountFromStoreTemplates()
    {
        using var dir = new TempContentDirectory();
        WriteMinimalTimeline(dir);
        dir.WriteFile("store-templates.json", """
            { "playerSlotBaseCost": 100, "playerSlotCostPerTier": 50, "playerSlotCount": 1 }
            """);

        var world = ContentLoader.LoadTimeWorld(dir.Path, worldSeed: 1);
        var vacant = world.GetYear(2500).StoreSlots.Count(s => s.IsAvailableForPurchase);

        Assert.Equal(1, vacant);
    }

    [Fact]
    public void LoadTimeWorld_ReadsPlayerSlotCostFromStoreTemplates()
    {
        using var dir = new TempContentDirectory();
        WriteMinimalTimeline(dir);
        dir.WriteFile("store-templates.json", """
            { "playerSlotBaseCost": 250, "playerSlotCostPerTier": 0, "playerSlotCount": 1 }
            """);

        var world = ContentLoader.LoadTimeWorld(dir.Path, worldSeed: 1);
        var slot = world.GetYear(2500).StoreSlots.Single(s => s.IsAvailableForPurchase);

        Assert.Equal(250, slot.PurchaseCost);
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
