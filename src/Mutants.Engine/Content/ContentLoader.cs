using System.Text.Json;
using Mutants.Core.Classes;
using Mutants.Core.Items;
using Mutants.Core.Time;

namespace Mutants.Engine.Content;

/// <summary>
/// Turns Mutants.Content's JSON files into live Core domain objects —
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
    /// Loads the continuous-timeline world (Mutants.Core.Time) from a
    /// content directory expected to contain <c>monster-species.json</c>,
    /// <c>item-archetypes.json</c>, <c>eras.json</c>, and (optionally)
    /// <c>store-templates.json</c>. <paramref name="worldSeed"/> is the
    /// per-save seed that fixes the Gatekeeper schedule and every year's
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

        var archetypes = ReadJson<List<ItemArchetypeData>>(Path.Combine(contentDirectory, "item-archetypes.json"))
            .Select(ToArchetype)
            .ToList();

        var eras = ReadJson<List<EraData>>(Path.Combine(contentDirectory, "eras.json"))
            .Select(ToEra)
            .ToList();

        var templatePath = Path.Combine(contentDirectory, "store-templates.json");
        var template = File.Exists(templatePath) ? ReadJson<StoreTemplateData>(templatePath) : new StoreTemplateData();
        var storeTemplate = new StoreStockTemplate(template.PlayerSlotBaseCost, template.PlayerSlotCostPerTier);

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

    /// <summary>Reads <c>npc-population.json</c> — a single <c>{ "totalCount": N }</c>. Returns the count, defaulting to 12 if the field is absent.</summary>
    public static int LoadNpcCount(string path) => Math.Max(0, ReadJson<NpcPopulationConfig>(path).TotalCount);

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

        if (!Enum.TryParse<Rarity>(data.Rarity, ignoreCase: true, out var rarity))
        {
            throw new ContentException($"Item archetype '{data.Id}': unknown rarity '{data.Rarity}'.");
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

        return new ItemArchetypeDefinition(
            data.Id, data.Name, type, rarity, restrictedClass,
            effect, data.EffectMagnitude, data.EffectDurationTicks, data.ThemeTags);
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
