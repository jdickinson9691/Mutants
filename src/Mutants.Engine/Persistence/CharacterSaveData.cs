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
    public int UnlockedTimeLevel { get; set; }
    public int CurrentTimeLevel { get; set; }
    public int PositionEast { get; set; }
    public int PositionNorth { get; set; }
    public List<int> DefeatedGatekeepers { get; set; } = [];
    public List<ItemSaveData> Inventory { get; set; } = [];

    /// <summary>Index into <see cref="Inventory"/> of the equipped weapon, or null.</summary>
    public int? EquippedWeaponIndex { get; set; }

    /// <summary>Index into <see cref="Inventory"/> of the equipped armor, or null.</summary>
    public int? EquippedArmorIndex { get; set; }

    public DateTime SavedAtUtc { get; set; }
}
