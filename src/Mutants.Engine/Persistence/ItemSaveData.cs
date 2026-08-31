namespace Mutants.Engine.Persistence;

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

    /// <summary>One of Mutants.Core.Items.ConsumableEffectType's names; "None" for non-consumables. Additive field — old blobs without it deserialize as "None".</summary>
    public string ConsumableEffect { get; set; } = "None";

    public double EffectMagnitude { get; set; }
    public int EffectDurationTicks { get; set; }
}
