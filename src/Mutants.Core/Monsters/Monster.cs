using Mutants.Core.Ions;
using Mutants.Core.Items;
using Mutants.Core.Stats;
using Mutants.Core.World;

namespace Mutants.Core.Monsters;

/// <summary>
/// A monster — the loot/XP source docs/GDD.md §5/§7 describes, and (per
/// §7) mechanically the same kind of opponent a rival Mutant NPC is.
/// Monsters are placed spatially in the year the player is standing in
/// (see <see cref="Mutants.Core.Time.YearPopulation"/>): they carry a
/// grid <see cref="Position"/>, an <see cref="Ions"/> pool they spend on
/// <see cref="Heal"/> (topping it up with <see cref="Convert"/> when
/// they've picked loot off the ground), and can wander / fight each other
/// under <c>Mutants.Engine.Npc.MonsterController</c>.
/// </summary>
public sealed class Monster
{
    public string Name { get; }
    public int Tier { get; }
    public HealthPool Health { get; }
    public IonPool Ions { get; }
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

    /// <summary>Current grid position within its year's map — set by <see cref="PlaceAt"/> / <see cref="MoveTo"/>.</summary>
    public Coordinate Position { get; private set; } = Coordinate.Origin;

    /// <summary>A defence penalty applied by a ranged <see cref="Items.RangedEffectType.Weaken"/> shot, consumed once by the next Mutants.Engine.Combat.CombatSession against this monster.</summary>
    public int PendingDefensePenalty { get; set; }

    /// <summary>
    /// How annoyed this monster is with the player — 0 = indifferent. Raised
    /// by the player entering/lingering on its tile or shooting it (see
    /// <see cref="AggroModel"/>), decays when the player leaves. Drives
    /// whether it pursues or ambushes. Session state — not saved.
    /// </summary>
    public double Aggro { get; private set; }

    /// <summary>Adds to <see cref="Aggro"/>, clamped to <see cref="AggroModel.Cap"/>.</summary>
    public void RaiseAggro(double amount) => Aggro = Math.Clamp(Aggro + Math.Max(0, amount), 0, AggroModel.Cap);

    /// <summary>Bleeds off <see cref="Aggro"/>, never below 0.</summary>
    public void DecayAggro(double amount) => Aggro = Math.Max(0, Aggro - Math.Max(0, amount));

    private readonly List<Item> _inventory = [];

    /// <summary>Items this monster is carrying — picked up off the ground, spent via <see cref="Convert"/>, and dropped where it dies.</summary>
    public IReadOnlyList<Item> Inventory => _inventory;

    private int _ticksSinceIonRegen;

    public Monster(
        string name,
        int tier,
        int maxHp,
        int attackPower,
        int defense,
        int speed,
        int xpReward,
        IReadOnlyList<LootTableEntry>? lootTable = null,
        IReadOnlyList<string>? tags = null,
        int? maxIons = null)
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
        Ions = new IonPool(maxIons ?? MonsterScaling.BaseIons(tier));
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

    /// <summary>Places the monster at a grid position — e.g. when its year's population is seeded.</summary>
    public void PlaceAt(Coordinate coordinate) => Position = coordinate;

    /// <summary>Moves the monster to an adjacent coordinate. Legality is the caller's job (see <see cref="World.LevelMap.TryMove"/>).</summary>
    public void MoveTo(Coordinate coordinate) => Position = coordinate;

    public void AddToInventory(Item item) => _inventory.Add(item);

    public bool RemoveFromInventory(Item item) => _inventory.Remove(item);

    /// <summary>
    /// Heals by spending Ions — the same rules as
    /// <see cref="Mutants.Core.Characters.Mutant.Heal"/>: HP restored at
    /// <see cref="IonEconomy.HpPerIonHealed"/> per Ion, capped by both
    /// missing HP and available Ions, never overhealing or overspending.
    /// Returns HP restored (0 if full or out of Ions — no Ions spent then).
    /// </summary>
    public int Heal()
    {
        var missingHp = Health.Max - Health.Current;
        if (missingHp <= 0)
        {
            return 0;
        }

        var ionsNeeded = (int)Math.Ceiling(missingHp / (double)IonEconomy.HpPerIonHealed);
        var ionsToSpend = Math.Min(ionsNeeded, Ions.Current);
        if (ionsToSpend <= 0)
        {
            return 0;
        }

        Ions.Spend(ionsToSpend);
        return Health.Heal(ionsToSpend * IonEconomy.HpPerIonHealed);
    }

    /// <summary>Destroys a carried item for Ions — same as <see cref="Mutants.Core.Characters.Mutant.Convert"/>. Returns the Ions actually gained (may be less than the item's convert value if the pool would overflow).</summary>
    public int Convert(Item item)
    {
        if (!_inventory.Remove(item))
        {
            throw new InvalidOperationException($"'{item.Name}' is not in {Name}'s inventory.");
        }

        return Ions.Add(item.ConvertValue());
    }

    /// <summary>Passive Ion regen — one <see cref="IonPool.Add"/> of 1 every <paramref name="ticksPerRegen"/> ticks. Monsters get regen only (no drain — that's a player survival tax). Returns true if an Ion was added.</summary>
    public bool AdvanceIonRegenTick(int ticksPerRegen)
    {
        if (ticksPerRegen < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ticksPerRegen), ticksPerRegen, "Ticks per regen must be at least 1.");
        }

        _ticksSinceIonRegen++;
        if (_ticksSinceIonRegen < ticksPerRegen)
        {
            return false;
        }

        _ticksSinceIonRegen = 0;
        return Ions.Add(1) > 0;
    }
}
