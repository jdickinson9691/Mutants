using Mutants.Core.Items;

namespace Mutants.Core.Time;

/// <summary>
/// A tiny hand-built <see cref="TimeWorld"/> — three eras, a handful of
/// species and item archetypes, a fixed seed. Engine/console sandbox
/// only (the console falls back to this if <c>Mutants.Content</c> fails
/// to load), and a convenient fixture for tests that need a real world
/// without file I/O. NOT launch content — the shipped catalogs live in
/// <c>Mutants.Content</c>.
/// </summary>
public static class TestTimeWorld
{
    public const long DefaultSeed = 20000305L;

    public static TimeWorld Build(long seed = DefaultSeed) =>
        new(seed, BuildEras(), BuildSpecies(), BuildItemArchetypes(), StoreStockTemplate.Default);

    private static EraTable BuildEras() => new(
    [
        new EraDefinition(
            FromYear: 2000,
            Name: "Ruined City",
            RoomText:
            [
                "You are standing at the crossroads of a ruined city block.",
                "You're in a maintenance shop, shelves stripped bare.",
                "You see rubble everywhere; the street has collapsed into it.",
                "You feel a cold breeze cutting between the buildings.",
            ],
            SpeciesIds: ["scavenger", "rubble-brute", "scrapyard-wraith"],
            ItemThemeTags: ["scrap", "common"]),
        new EraDefinition(
            FromYear: 3000,
            Name: "Ashfall Wastes",
            RoomText:
            [
                "Ash drifts in slow spirals over a dead highway.",
                "A skeletal billboard creaks in the wind.",
                "Sand has swallowed half of what was once a plaza.",
            ],
            SpeciesIds: ["dune-stalker", "ash-wraith", "ashfall-behemoth"],
            ItemThemeTags: ["ash", "common"]),
        new EraDefinition(
            FromYear: 4200,
            Name: "The Chronofracture",
            RoomText:
            [
                "Reality stutters; the same moment happens twice.",
                "A corridor folds back into itself here.",
                "Shards of frozen time hang in the air.",
            ],
            SpeciesIds: ["fracture-wisp", "paradox-wraith", "bulwark-construct"],
            ItemThemeTags: ["paradox", "common"]),
    ]);

    private static IReadOnlyList<SpeciesDefinition> BuildSpecies() =>
    [
        new SpeciesDefinition("scavenger", "Scavenger", [], MonsterArchetype.Baseline, ["scrap", "common"]),
        new SpeciesDefinition("rubble-brute", "Rubble Brute", [], MonsterArchetype.Bruiser, ["scrap", "common"]),
        new SpeciesDefinition("scrapyard-wraith", "Scrapyard Wraith", ["undead"], MonsterArchetype.Caster, ["scrap"]),
        new SpeciesDefinition("dune-stalker", "Dune Stalker", [], MonsterArchetype.Skirmisher, ["ash", "common"]),
        new SpeciesDefinition("ash-wraith", "Ash Wraith", ["undead"], MonsterArchetype.Caster, ["ash"]),
        new SpeciesDefinition("ashfall-behemoth", "Ashfall Behemoth", [], MonsterArchetype.Bruiser, ["ash", "common"]),
        new SpeciesDefinition("fracture-wisp", "Fracture Wisp", [], MonsterArchetype.Skirmisher, ["paradox", "common"]),
        new SpeciesDefinition("paradox-wraith", "Paradox Wraith", ["undead"], MonsterArchetype.Caster, ["paradox"]),
        new SpeciesDefinition("bulwark-construct", "Bulwark Construct", [], MonsterArchetype.Bruiser, ["paradox", "common"]),
    ];

    private static IReadOnlyList<ItemArchetypeDefinition> BuildItemArchetypes() =>
    [
        Weapon("blade", "Salvaged Blade", Rarity.Common, ["scrap", "ash", "paradox", "common"]),
        Weapon("warrior-arm", "Bruiser's Maul", Rarity.Uncommon, ["scrap", "ash", "paradox"], CharacterClassRestriction: Classes.CharacterClass.Warrior),
        Armor("plate", "Layered Plating", Rarity.Common, ["scrap", "ash", "paradox", "common"]),
        Junk("shard", "Salvage Shard", Rarity.Common, ["scrap", "ash", "paradox", "common"]),
        Food("ration", "Ration Pack", magnitude: 12, ["scrap", "ash", "paradox", "common"]),
        Potion("stim", "Combat Stim", ConsumableEffectType.BuffAttack, magnitude: 4, ["scrap", "ash", "paradox", "common"]),
        Potion("ward", "Ward Draught", ConsumableEffectType.BuffDefense, magnitude: 4, ["scrap", "ash", "paradox", "common"]),
    ];

    private static ItemArchetypeDefinition Weapon(string id, string name, Rarity rarity, IReadOnlyList<string> themes, Classes.CharacterClass? CharacterClassRestriction = null) =>
        new(id, name, ItemType.Weapon, rarity, CharacterClassRestriction, ConsumableEffectType.None, 0, 0, themes);

    private static ItemArchetypeDefinition Armor(string id, string name, Rarity rarity, IReadOnlyList<string> themes) =>
        new(id, name, ItemType.Armor, rarity, null, ConsumableEffectType.None, 0, 0, themes);

    private static ItemArchetypeDefinition Junk(string id, string name, Rarity rarity, IReadOnlyList<string> themes) =>
        new(id, name, ItemType.Junk, rarity, null, ConsumableEffectType.None, 0, 0, themes);

    private static ItemArchetypeDefinition Food(string id, string name, double magnitude, IReadOnlyList<string> themes) =>
        new(id, name, ItemType.Consumable, Rarity.Common, null, ConsumableEffectType.Heal, magnitude, 0, themes);

    private static ItemArchetypeDefinition Potion(string id, string name, ConsumableEffectType effect, double magnitude, IReadOnlyList<string> themes) =>
        new(id, name, ItemType.Consumable, Rarity.Uncommon, null, effect, magnitude, 15, themes);
}
