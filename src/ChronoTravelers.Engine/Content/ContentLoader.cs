using System.Text.Json;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Time;

namespace ChronoTravelers.Engine.Content;

/// <summary>
/// Turns ChronoTravelers.Content's JSON files into live Core domain objects —
/// docs/TECH_STACK.md: "Data-driven content ... JSON or YAML content
/// files loaded at startup, deserialized with System.Text.Json." Content
/// data owns names/ids/numbers (docs/AGENTS.md's Content Agent); this
/// class owns turning that into the <see cref="TimeWorld"/> the rest of
/// the engine works with, plus the ability tables and the NPC-count
/// config.
/// </summary>
public static class ContentLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Loads the continuous-timeline world (ChronoTravelers.Core.Time) from a
    /// content directory expected to contain <c>monster-species.json</c>,
    /// <c>item-archetypes.json</c>, <c>eras.json</c>, and (optionally)
    /// <c>store-templates.json</c>. <paramref name="worldSeed"/> is the
    /// per-save seed that fixes the Warden schedule and every year's
    /// map/store layout. Throws <see cref="ContentException"/> on a
    /// missing/malformed file, an unknown enum value, or a failed
    /// cross-reference (the <see cref="EraTable"/> / <see cref="TimeWorld"/>
    /// validation surfaced as content errors).
    /// </summary>
    public static TimeWorld LoadTimeWorld(string contentDirectory, long worldSeed)
    {
        var species = ReadJson<List<MonsterSpeciesData>>(Path.Combine(contentDirectory, "monster-species.json"))
            .Select(ToSpecies)
            .ToList();

        var archetypes = LoadItemArchetypes(Path.Combine(contentDirectory, "item-archetypes.json"));

        var eras = ReadJson<List<EraData>>(Path.Combine(contentDirectory, "eras.json"))
            .Select(ToEra)
            .ToList();

        var templatePath = Path.Combine(contentDirectory, "store-templates.json");
        var template = File.Exists(templatePath) ? ReadJson<StoreTemplateData>(templatePath) : new StoreTemplateData();
        var storeTemplate = new StoreStockTemplate(template.PlayerSlotBaseCost, template.PlayerSlotCostPerTier, template.PlayerSlotCount);

        EraTable eraTable;
        try
        {
            eraTable = new EraTable(eras);
        }
        catch (ArgumentException ex)
        {
            throw new ContentException($"eras.json: {ex.Message}", ex);
        }

        try
        {
            return new TimeWorld(worldSeed, eraTable, species, archetypes, storeTemplate);
        }
        catch (ArgumentException ex)
        {
            throw new ContentException($"timeline content is inconsistent: {ex.Message}", ex);
        }
    }

    public static IReadOnlyList<AbilityData> LoadAbilities(string path) => ReadJson<List<AbilityData>>(path);

    /// <summary>Reads and validates <c>item-archetypes.json</c> into live definitions (equippable rarity derived from <c>powerMultiplier</c>). Throws <see cref="ContentException"/> on a bad enum / out-of-range multiplier.</summary>
    public static IReadOnlyList<ItemArchetypeDefinition> LoadItemArchetypes(string path) =>
        ReadJson<List<ItemArchetypeData>>(path).Select(ToArchetype).ToList();

    /// <summary>Reads <c>npc-population.json</c> — a single <c>{ "totalCount": N }</c>. Returns the count, defaulting to 12 if the field is absent.</summary>
    public static int LoadNpcCount(string path) => Math.Max(0, ReadJson<NpcPopulationConfig>(path).TotalCount);

    /// <summary>
    /// Reads <c>npc-population.json</c>'s optional <c>classWeights</c> map
    /// (docs/CONTENT_PLAN.md's "config-driven NPC class distribution")
    /// into a <see cref="CharacterClass"/>-keyed table for
    /// <see cref="ChronoTravelers.Engine.Npc.NpcPopulation"/>. Returns null
    /// when the field is absent or empty, meaning "not configured" — callers
    /// pass that straight through and get the original uniform-random pick.
    /// Throws <see cref="ContentException"/> if a key isn't a recognized
    /// <see cref="CharacterClass"/> name.
    /// </summary>
    public static IReadOnlyDictionary<CharacterClass, double>? LoadNpcClassWeights(string path)
    {
        var raw = ReadJson<NpcPopulationConfig>(path).ClassWeights;
        if (raw is null || raw.Count == 0)
        {
            return null;
        }

        var weights = new Dictionary<CharacterClass, double>();
        foreach (var (key, value) in raw)
        {
            if (!Enum.TryParse<CharacterClass>(key, ignoreCase: true, out var characterClass))
            {
                throw new ContentException($"npc-population.json: unknown class '{key}' in classWeights.");
            }

            weights[characterClass] = value;
        }

        return weights;
    }

    private static SpeciesDefinition ToSpecies(MonsterSpeciesData data)
    {
        if (!Enum.TryParse<MonsterArchetype>(data.Archetype, ignoreCase: true, out var archetype))
        {
            throw new ContentException($"Species '{data.Id}': unknown archetype '{data.Archetype}'.");
        }

        return new SpeciesDefinition(data.Id, data.Name, data.Tags, archetype, data.LootThemeTags);
    }

    private static ItemArchetypeDefinition ToArchetype(ItemArchetypeData data)
    {
        if (!Enum.TryParse<ItemType>(data.Type, ignoreCase: true, out var type))
        {
            throw new ContentException($"Item archetype '{data.Id}': unknown type '{data.Type}'.");
        }

        CharacterClass? restrictedClass = null;
        if (data.RestrictedClass is not null)
        {
            if (!Enum.TryParse<CharacterClass>(data.RestrictedClass, ignoreCase: true, out var parsed))
            {
                throw new ContentException($"Item archetype '{data.Id}': unknown restrictedClass '{data.RestrictedClass}'.");
            }

            restrictedClass = parsed;
        }

        if (!Enum.TryParse<ConsumableEffectType>(data.Effect, ignoreCase: true, out var effect))
        {
            throw new ContentException($"Item archetype '{data.Id}': unknown effect '{data.Effect}'.");
        }

        if (!Enum.TryParse<RangedKind>(data.RangedKind, ignoreCase: true, out var rangedKind))
        {
            throw new ContentException($"Item archetype '{data.Id}': unknown rangedKind '{data.RangedKind}'.");
        }

        if (!Enum.TryParse<RangedEffectType>(data.RangedEffect, ignoreCase: true, out var rangedEffect))
        {
            throw new ContentException($"Item archetype '{data.Id}': unknown rangedEffect '{data.RangedEffect}'.");
        }

        if (rangedKind != RangedKind.None && type != ItemType.Ranged)
        {
            throw new ContentException($"Item archetype '{data.Id}': rangedKind '{data.RangedKind}' requires type 'Ranged' (got '{data.Type}').");
        }

        if (rangedKind == RangedKind.None && type == ItemType.Ranged)
        {
            throw new ContentException($"Item archetype '{data.Id}': type 'Ranged' requires a rangedKind ('Wand', 'Bow', or 'Gun').");
        }

        var isEquippable = type is ItemType.Weapon or ItemType.Armor || rangedKind != RangedKind.None;

        Rarity rarity;
        var powerMultiplier = 1.0;
        if (isEquippable)
        {
            // Rarity is DERIVED from power for equippables — the JSON's
            // `rarity` (if any) is ignored on purpose (docs/GDD.md §5).
            powerMultiplier = data.PowerMultiplier;
            if (powerMultiplier is < LootScaling.MinPowerMultiplier or > LootScaling.MaxPowerMultiplier)
            {
                throw new ContentException(
                    $"Item archetype '{data.Id}': powerMultiplier {powerMultiplier} is outside " +
                    $"[{LootScaling.MinPowerMultiplier}, {LootScaling.MaxPowerMultiplier}].");
            }

            rarity = RarityExtensions.ForPower(powerMultiplier);
        }
        else
        {
            if (!Enum.TryParse(data.Rarity, ignoreCase: true, out rarity))
            {
                throw new ContentException($"Item archetype '{data.Id}': unknown rarity '{data.Rarity}'.");
            }
        }

        return new ItemArchetypeDefinition(
            data.Id, data.Name, type, rarity, restrictedClass,
            effect, data.EffectMagnitude, data.EffectDurationTicks, data.ThemeTags,
            rangedKind, data.AmmoCapacity, rangedEffect, powerMultiplier);
    }

    private static EraDefinition ToEra(EraData data) =>
        new(data.FromYear, data.Name, data.RoomText, data.SpeciesIds, data.ItemThemeTags);

    private static T ReadJson<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new ContentException($"Content file not found: {path}");
        }

        using var stream = File.OpenRead(path);
        try
        {
            return JsonSerializer.Deserialize<T>(stream, JsonOptions)
                ?? throw new ContentException($"'{path}' deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new ContentException($"'{path}' is not valid JSON for the expected shape: {ex.Message}", ex);
        }
    }
}
