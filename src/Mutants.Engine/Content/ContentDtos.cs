namespace Mutants.Engine.Content;

// Plain, System.Text.Json-friendly shapes mirroring the JSON files in
// Mutants.Content — docs/AGENTS.md's Content Agent owns that JSON;
// ContentLoader (this namespace) owns turning it into live Core domain
// objects. Deliberately separate from the save-file DTOs in
// Persistence — different concern, different lifecycle.

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

/// <summary><c>npc-population.json</c> — a single total NPC count for the whole timeline (they're scattered across it, not bucketed per level any more).</summary>
public sealed class NpcPopulationConfig
{
    public int TotalCount { get; set; } = 12;
}

// ---------------------------------------------------------------------------
// Continuous-timeline content (Mutants.Core.Time): tier-free catalogs the
// year-based world generator scales on the fly. See ContentLoader.LoadTimeWorld.
// ---------------------------------------------------------------------------

/// <summary>A monster species — <c>monster-species.json</c>. No stats/tier: <see cref="Archetype"/> + the encounter year produce them.</summary>
public sealed class MonsterSpeciesData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Tags { get; set; } = [];

    /// <summary>One of Mutants.Core.Time.MonsterArchetype's names: "Baseline", "Caster", "Bruiser", "Skirmisher".</summary>
    public string Archetype { get; set; } = "";

    /// <summary>Item theme tags this species can drop — matched against <see cref="ItemArchetypeData.ThemeTags"/>.</summary>
    public List<string> LootThemeTags { get; set; } = [];
}

/// <summary>An item archetype — <c>item-archetypes.json</c>. No tier: value/bonuses come from the year it drops in.</summary>
public sealed class ItemArchetypeData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";

    /// <summary>Authored rarity — used for Consumable/Junk only. For an equippable (Weapon/Armor/Ranged) rarity is derived from <see cref="PowerMultiplier"/> and this is ignored.</summary>
    public string Rarity { get; set; } = "";

    /// <summary>
    /// Equippables only (Weapon/Armor/Ranged): scales the tier baseline
    /// for the AttackBonus/DefenseBonus and, via Rarity.ForPower, fixes the
    /// item's rarity. ~0.5 = crude, 1.0 = standard, ~1.75 = fine, ~2.8 =
    /// relic. Defaults to 1.0 (Standard/Uncommon) when omitted.
    /// </summary>
    public double PowerMultiplier { get; set; } = 1.0;

    public string? RestrictedClass { get; set; }

    /// <summary>One of Mutants.Core.Items.ConsumableEffectType's names; defaults to "None".</summary>
    public string Effect { get; set; } = "None";

    public double EffectMagnitude { get; set; }
    public int EffectDurationTicks { get; set; }

    /// <summary>One of Mutants.Core.Items.RangedKind's names ("Wand"/"Bow"/"Gun"); "None" for a non-ranged archetype. When set, <c>type</c> must be "Ranged". Additive — old JSON without it loads as "None".</summary>
    public string RangedKind { get; set; } = "None";

    /// <summary>Built-in shot count for a ranged archetype. Ignored when <see cref="RangedKind"/> is "None".</summary>
    public int AmmoCapacity { get; set; }

    /// <summary>One of Mutants.Core.Items.RangedEffectType's names ("Weaken"); "None" for a damage-only ranged weapon. <see cref="EffectMagnitude"/> doubles as the ranged damage multiplier / Weaken amount.</summary>
    public string RangedEffect { get; set; } = "None";

    public List<string> ThemeTags { get; set; } = [];
}

/// <summary>One era band of the timeline — <c>eras.json</c>. Ordered by <see cref="FromYear"/>; the first must be 2000.</summary>
public sealed class EraData
{
    public int FromYear { get; set; }
    public string Name { get; set; } = "";
    public List<string> RoomText { get; set; } = [];
    public List<string> SpeciesIds { get; set; } = [];
    public List<string> ItemThemeTags { get; set; } = [];
}

/// <summary>Store tuning — <c>store-templates.json</c>. Optional; sensible defaults if the file is absent.</summary>
public sealed class StoreTemplateData
{
    public int PlayerSlotBaseCost { get; set; } = 100;
    public int PlayerSlotCostPerTier { get; set; } = 110;
}
