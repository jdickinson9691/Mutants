using Mutants.Core.Stats;

namespace Mutants.Core.Monsters;

/// <summary>
/// A monster — the loot/XP source docs/GDD.md §5/§7 describes, and (per
/// §7) mechanically the same kind of opponent a rival Mutant NPC is,
/// combat-wise. Real monster rosters are future Content Agent work per
/// docs/CONTENT_PLAN.md ("Monster roster per time-travel level"); this
/// type is just the shape.
/// </summary>
public sealed class Monster
{
    public string Name { get; }
    public int Tier { get; }
    public HealthPool Health { get; }
    public int AttackPower { get; }
    public int Defense { get; }
    public int Speed { get; }
    public int XpReward { get; }
    public IReadOnlyList<LootTableEntry> LootTable { get; }

    /// <summary>
    /// Free-form tags (e.g. "undead") — docs/CONTENT_PLAN.md's monster
    /// roster item calls these out explicitly, matched against by tag-
    /// conditioned abilities like Priest's Turn Undead.
    /// </summary>
    public IReadOnlyList<string> Tags { get; }

    public Monster(
        string name,
        int tier,
        int maxHp,
        int attackPower,
        int defense,
        int speed,
        int xpReward,
        IReadOnlyList<LootTableEntry>? lootTable = null,
        IReadOnlyList<string>? tags = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name cannot be empty.", nameof(name));
        }

        if (tier < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(tier), tier, "Tier must be at least 1.");
        }

        Name = name;
        Tier = tier;
        Health = new HealthPool(maxHp);
        AttackPower = attackPower;
        Defense = defense;
        Speed = speed;
        XpReward = xpReward;
        LootTable = lootTable ?? [];
        Tags = tags ?? [];
    }

    public bool HasTag(string tag) => Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);

    /// <summary>Builds a monster from <see cref="MonsterScaling"/>'s tier baselines instead of specifying stats directly.</summary>
    public static Monster Create(string name, int tier, IReadOnlyList<LootTableEntry>? lootTable = null, IReadOnlyList<string>? tags = null) =>
        new(name, tier,
            maxHp: MonsterScaling.BaseHp(tier),
            attackPower: MonsterScaling.BaseAttackPower(tier),
            defense: MonsterScaling.BaseDefense(tier),
            speed: MonsterScaling.BaseSpeed(tier),
            xpReward: MonsterScaling.XpReward(tier),
            lootTable: lootTable,
            tags: tags);
}
