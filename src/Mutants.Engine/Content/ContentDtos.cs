namespace Mutants.Engine.Content;

// Plain, System.Text.Json-friendly shapes mirroring the JSON files in
// Mutants.Content — docs/AGENTS.md's Content Agent owns that JSON;
// ContentLoader (this namespace) owns turning it into live Core domain
// objects. Deliberately separate from the save-file DTOs in
// Persistence — different concern, different lifecycle.

public sealed class CoordinateData
{
    public int East { get; set; }
    public int North { get; set; }
}

public sealed class ItemTemplateData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public int Tier { get; set; }
    public string Rarity { get; set; } = "";
    public string? RestrictedClass { get; set; }
}

public sealed class LootEntryData
{
    public string ItemId { get; set; } = "";
    public double DropChance { get; set; }
}

public sealed class MonsterTemplateData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Tier { get; set; }
    public List<string> Tags { get; set; } = [];
    public int MaxHp { get; set; }
    public int AttackPower { get; set; }
    public int Defense { get; set; }
    public int Speed { get; set; }
    public int XpReward { get; set; }
    public List<LootEntryData> LootTable { get; set; } = [];
}

public sealed class RoomData
{
    public int East { get; set; }
    public int North { get; set; }
    public string Description { get; set; } = "";

    /// <summary>Direction name ("North"/"South"/"East"/"West") -> exit flavor text. Explicit per room, not auto-inferred, so authors control connectivity directly.</summary>
    public Dictionary<string, string> Exits { get; set; } = [];
}

public sealed class LevelData
{
    public int LevelNumber { get; set; }
    public string Name { get; set; } = "";
    public CoordinateData Start { get; set; } = new();
    public int MinCharacterLevelToUnlock { get; set; } = 1;
    public string? GatekeeperMonsterId { get; set; }
    public List<string> MonsterRosterIds { get; set; } = [];
    public List<RoomData> Rooms { get; set; } = [];
}

public sealed class StoreListingData
{
    public string ItemId { get; set; } = "";
    public int AskingPrice { get; set; }
}

public sealed class StoreSlotData
{
    public int LevelNumber { get; set; }
    public string Name { get; set; } = "";
    public CoordinateData Location { get; set; } = new();
    public int PurchaseCost { get; set; }
    public bool IsGovernment { get; set; }
    public List<StoreListingData> Listings { get; set; } = [];
}

/// <summary>
/// One ability tier for one class — content only. Nothing in the engine
/// executes abilities yet (combat is still primary-attack-only); this
/// exists so the docs/GDD.md §4.2 tables have a real, load-ready home
/// instead of living only as GDD prose, ahead of that execution work.
/// </summary>
public sealed class AbilityData
{
    public string Class { get; set; } = "";
    public int Tier { get; set; }
    public int Level { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>"gdd" for a docs/GDD.md §4.2-specified ability, "original" for a class the GDD explicitly left as "full tables live in docs/CONTENT_PLAN.md" — keeps the project's [SOURCE] vs. original-design convention alive in data, not just prose.</summary>
    public string Source { get; set; } = "original";
}

public sealed class NpcPopulationData
{
    public int LevelNumber { get; set; }
    public int Count { get; set; }
    public int MinLevel { get; set; } = 1;
    public int MaxLevel { get; set; } = 1;
}
