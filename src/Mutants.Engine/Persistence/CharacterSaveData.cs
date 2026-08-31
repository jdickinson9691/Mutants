namespace Mutants.Engine.Persistence;

/// <summary>
/// Plain, LiteDB-friendly save-file shape of a Mutant — docs/AGENTS.md:
/// Engine "must not change the public save-file schema without a
/// migration path." Only the player's own character is saved this way
/// (NPCs are re-simulated fresh each session — see docs/CONTENT_PLAN.md-
/// adjacent scoping notes in Mutants.Console); NPCs still contribute to
/// <see cref="LeaderboardEntry"/> history, just not full character saves.
/// </summary>
public sealed class CharacterSaveData
{
    /// <summary>Bumped when the shape changes incompatibly. 1 = the old discrete-level schema; 2 = the continuous-year timeline. See CharacterMapper.FromSaveData for the 1→2 migration.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public const int CurrentSchemaVersion = 2;

    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Class { get; set; } = "";
    public int Level { get; set; }
    public int Xp { get; set; }
    public int Strength { get; set; }
    public int Agility { get; set; }
    public int Faith { get; set; }
    public int Intellect { get; set; }
    public int CurrentHp { get; set; }
    public int MaxHp { get; set; }
    public int CurrentIons { get; set; }
    public int MaxIons { get; set; }
    public int Riblets { get; set; }

    /// <summary>The per-save world seed — fixes the Gatekeeper schedule and every year's map/store layout (Mutants.Core.Time.TimeWorld). 0 on a legacy (schema 1) blob.</summary>
    public long WorldSeed { get; set; }

    /// <summary>The year the character is standing in (2000–5000).</summary>
    public int CurrentYear { get; set; }

    /// <summary>The furthest-future year ever reached — drives the soft level cap.</summary>
    public int FurthestYearReached { get; set; }

    public int PositionEast { get; set; }
    public int PositionNorth { get; set; }

    /// <summary>The Gatekeeper years the character has cleared (schema 2). Legacy blobs carry level numbers here; the migration discards them.</summary>
    public List<int> DefeatedGatekeepers { get; set; } = [];

    // --- Legacy (schema 1) fields, read only for migration ---------------
    /// <summary>Legacy: the deepest unlocked discrete level. Migrated to <see cref="FurthestYearReached"/>.</summary>
    public int UnlockedTimeLevel { get; set; }

    /// <summary>Legacy: the discrete level the character was standing in. Migrated to <see cref="CurrentYear"/>.</summary>
    public int CurrentTimeLevel { get; set; }

    public List<ItemSaveData> Inventory { get; set; } = [];

    /// <summary>Index into <see cref="Inventory"/> of the equipped weapon, or null.</summary>
    public int? EquippedWeaponIndex { get; set; }

    /// <summary>Index into <see cref="Inventory"/> of the equipped armor, or null.</summary>
    public int? EquippedArmorIndex { get; set; }

    public DateTime SavedAtUtc { get; set; }
}
