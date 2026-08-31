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

    /// <summary>One of Mutants.Core.Items.ConsumableEffectType's names ("None", "Heal", "BuffAttack", "BuffDefense") — only meaningful for a Consumable item; defaults to "None" (flavor-only) if omitted.</summary>
    public string Effect { get; set; } = "None";

    /// <summary>Meaning depends on Effect: flat HP for Heal, a flat stat bonus for BuffAttack/BuffDefense.</summary>
    public double EffectMagnitude { get; set; }

    /// <summary>How many world ticks a BuffAttack/BuffDefense effect lasts. Unused (and should stay 0) for Heal, which is instant.</summary>
    public int EffectDurationTicks { get; set; }
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
/// One ability tier for one class. Flavor (Name/Description/Source) plus
/// mechanical parameters consumed by Mutants.Engine.Combat.CombatSession
/// — tunable numbers belong in content, not engine code, per
/// docs/AGENTS.md's "data over code" rule. Several abilities were written
/// for group combat (docs/GDD.md's "hit up to 2 additional adjacent
/// enemies," "heal ... adjacent party/NPC allies") that this engine
/// doesn't have yet (fights are strictly one Mutant vs. one Monster) —
/// those are deliberately adapted to a single-target equivalent rather
/// than left unimplemented; a genuine multi-combatant fight is future
/// work. A few (Fence's Favor, Mana Well, Blink, Resurrect Lite) have no
/// combat effect at all — passive/economy/overworld/party mechanics none
/// of which exist yet — and are marked Effect "None".
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

    /// <summary>Ions spent to cast — 0 for Second Wind, matching the GDD's explicit "no Ions."</summary>
    public int IonCost { get; set; }

    /// <summary>
    /// One of: Damage, IgnoreDefenseDamage, Heal, BuffSelfAttack,
    /// BuffSelfDefense, DebuffTargetAttack, DebuffTargetDefense,
    /// DebuffTargetSpeed, GuaranteedCritNextAttack, ExtraAttack, Shield,
    /// DamageOverTime, RestoreIons, InstantDefeatNonBoss, None. See
    /// Mutants.Engine.Combat.AbilityEffectType for what each does.
    /// </summary>
    public string Effect { get; set; } = "None";

    /// <summary>Meaning depends on Effect: a damage/heal multiplier, a flat buff/debuff amount, a DoT's per-round damage, etc.</summary>
    public double Magnitude { get; set; }

    /// <summary>Extra rounds a DamageOverTime effect keeps ticking after the cast round.</summary>
    public int DurationRounds { get; set; }

    /// <summary>Gates a Damage effect to only apply its bonus under a specific circumstance: "TargetUndamaged", "TargetBelow25Percent", "TargetTagged", or empty for no condition.</summary>
    public string Condition { get; set; } = "";

    /// <summary>The monster tag a "TargetTagged" condition checks for (e.g. "undead").</summary>
    public string? Tag { get; set; }
}

public sealed class NpcPopulationData
{
    public int LevelNumber { get; set; }
    public int Count { get; set; }
    public int MinLevel { get; set; } = 1;
    public int MaxLevel { get; set; } = 1;
}
