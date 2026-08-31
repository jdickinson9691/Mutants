namespace ChronTravelers.Engine.Persistence;

/// <summary>Plain, LiteDB-friendly save-file shape of an Item (docs/AGENTS.md: Engine "must not change the public save-file schema without a migration path" — this is that schema).</summary>
public sealed class ItemSaveData
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public int Tier { get; set; }
    public string Rarity { get; set; } = "";
    public int Value { get; set; }
    public int AttackBonus { get; set; }
    public int DefenseBonus { get; set; }
    public string? RestrictedClass { get; set; }

    /// <summary>One of ChronTravelers.Core.Items.ConsumableEffectType's names; "None" for non-consumables. Additive field — old blobs without it deserialize as "None".</summary>
    public string ConsumableEffect { get; set; } = "None";

    public double EffectMagnitude { get; set; }
    public int EffectDurationTicks { get; set; }

    // --- Ranged weapons (additive; old blobs deserialize as "None" / 0 / "") ---

    /// <summary>One of ChronTravelers.Core.Items.RangedKind's names; "None" for a non-ranged item.</summary>
    public string RangedKind { get; set; } = "None";

    public int AmmoCapacity { get; set; }

    /// <summary>Live shots left — the whole reason a ranged weapon needs per-instance save state.</summary>
    public int AmmoRemaining { get; set; }

    /// <summary>One of ChronTravelers.Core.Items.RangedEffectType's names; "None" for a damage-only ranged weapon.</summary>
    public string RangedEffect { get; set; } = "None";

    /// <summary>The ranged weapon's unique instance id (empty string for every non-ranged item).</summary>
    public string InstanceId { get; set; } = "";
}
