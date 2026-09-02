using ChronoTravelers.Core.Tachyons;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Stats;
using ChronoTravelers.Core.World;

namespace ChronoTravelers.Core.Monsters;

/// <summary>
/// A monster — the loot/XP source docs/GDD.md §5/§7 describes, and (per
/// §7) mechanically the same kind of opponent a rival Traveler NPC is.
/// Monsters are placed spatially in the year the player is standing in
/// (see <see cref="ChronoTravelers.Core.Time.YearPopulation"/>): they carry a
/// grid <see cref="Position"/>, an <see cref="Tachyons"/> pool they spend on
/// <see cref="Heal"/> (topping it up with <see cref="Convert"/> when
/// they've picked loot off the ground), and can wander / fight each other
/// under <c>ChronoTravelers.Engine.Npc.MonsterController</c>.
/// </summary>
public sealed class Monster
{
    /// <summary>
    /// The display name — the species name (<see cref="BaseName"/>) alone
    /// until <see cref="Enumerate"/> gives it a per-instance
    /// <c>"-###"</c> suffix (docs/GDD.md §7.1: "Ashfall Echo-042"), which
    /// every spatial monster in a year's population gets, so it's
    /// addressable individually — most importantly by
    /// <c>ChronoTravelers.Engine.Npc.MonsterController</c>'s periodic yell
    /// banter, which calls out one living monster by name to another.
    /// </summary>
    public string Name { get; private set; }

    /// <summary>The un-numbered species/kind name ("Ashfall Echo", "Junk Golem") — <see cref="Name"/> before <see cref="Enumerate"/> appends its instance suffix.</summary>
    public string BaseName { get; }

    public int Tier { get; }
    public HealthPool Health { get; }
    public TachyonPool Tachyons { get; }
    public int AttackPower { get; }
    public int Defense { get; }
    public int Speed { get; }
    public int XpReward { get; }
    public IReadOnlyList<LootTableEntry> LootTable { get; }

    /// <summary>
    /// An "apex" — a rare, much tougher monster seeded a few to a year
    /// (see <see cref="ChronoTravelers.Core.Time.YearPopulation"/>). It hits
    /// harder and soaks far more, drops better loot, and — crucially —
    /// accrues aggro at a fraction of the normal rate
    /// (<see cref="AggroModel.ApexAggroMultiplier"/>), so it essentially
    /// ignores a passer-by: the player picks the fight, or leaves it be.
    /// </summary>
    public bool IsApex { get; }

    /// <summary>
    /// Free-form tags (e.g. "echo") — docs/CONTENT_PLAN.md's monster
    /// roster item calls these out explicitly, matched against by tag-
    /// conditioned abilities like Doctor's Turn Undead.
    /// </summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>Current grid position within its year's map — set by <see cref="PlaceAt"/> / <see cref="MoveTo"/>.</summary>
    public Coordinate Position { get; private set; } = Coordinate.Origin;

    /// <summary>A defence penalty applied by a ranged <see cref="Items.RangedEffectType.Weaken"/> shot, consumed once by the next ChronoTravelers.Engine.Combat.CombatSession against this monster.</summary>
    public int PendingDefensePenalty { get; set; }

    /// <summary>An attack penalty applied by a ranged <see cref="Items.RangedEffectType.Stagger"/> shot, consumed once by the next ChronoTravelers.Engine.Combat.CombatSession against this monster — the offense-side counterpart to <see cref="PendingDefensePenalty"/>.</summary>
    public int PendingAttackPenalty { get; set; }

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

    /// <summary>
    /// Ticks left in a brief "settle" — a roaming monster occasionally
    /// pauses for a couple of turns. Set/decremented by
    /// ChronoTravelers.Engine.Npc.MonsterController; not used while pursuing.
    /// Session state — not saved.
    /// </summary>
    public int RestTicks { get; set; }

    /// <summary>
    /// The direction a roaming monster is currently patrolling. It keeps
    /// heading this way each move (rather than random-walking) until it's
    /// blocked or picks a new one — so you can read the <c>monsters</c>
    /// list, see where one is going, and cut it off. Session state.
    /// </summary>
    public Direction? Heading { get; set; }

    private readonly List<Item> _inventory = [];

    /// <summary>Items this monster is carrying — picked up off the ground, spent via <see cref="Convert"/>, and dropped where it dies.</summary>
    public IReadOnlyList<Item> Inventory => _inventory;

    private Item? _equippedWeapon;

    /// <summary>A weapon this monster scavenged off the ground because it beat what it was wielding — adds its <see cref="Item.AttackBonus"/> to <see cref="EffectiveAttackPower"/>, and drops with the rest of its inventory on death. Session state.</summary>
    public Item? EquippedWeapon => _equippedWeapon;

    /// <summary>Base <see cref="AttackPower"/> plus any scavenged <see cref="EquippedWeapon"/>'s bonus — what its hits actually land for.</summary>
    public int EffectiveAttackPower => AttackPower + (_equippedWeapon?.AttackBonus ?? 0);

    /// <summary>Wields <paramref name="weapon"/> (adding it to inventory if it isn't already there). Caller checks it's actually an upgrade.</summary>
    public void EquipWeapon(Item weapon)
    {
        if (weapon.Type != ItemType.Weapon)
        {
            throw new ArgumentException("Only a Weapon can be equipped.", nameof(weapon));
        }

        _equippedWeapon = weapon;
        if (!_inventory.Contains(weapon))
        {
            _inventory.Add(weapon);
        }
    }

    private int _ticksSinceTachyonRegen;

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
        int? maxTachyons = null,
        bool isApex = false)
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
        BaseName = name;
        Tier = tier;
        Health = new HealthPool(maxHp);
        Tachyons = new TachyonPool(maxTachyons ?? MonsterScaling.BaseTachyons(tier));
        AttackPower = attackPower;
        Defense = defense;
        Speed = speed;
        XpReward = xpReward;
        LootTable = lootTable ?? [];
        Tags = tags ?? [];
        IsApex = isApex;
    }

    private bool _enumerated;

    /// <summary>
    /// Gives this instance its <c>"-###"</c> callsign — a three-digit,
    /// zero-padded suffix on <see cref="BaseName"/> (e.g. "042"), wrapped
    /// into range from whatever <paramref name="number"/> the caller rolled
    /// (so a deterministic per-year RNG stream, like
    /// <see cref="YearPopulation.Seed"/>'s, reproduces the same callsigns on
    /// every re-seed of the same world/year — <see cref="Name"/> itself
    /// isn't part of any seed's determinism contract before this is called,
    /// only after). A no-op past the first call — every real call site
    /// enumerates exactly once, right after construction; this just makes
    /// a stray extra call harmless rather than silently re-rolling the
    /// player-visible name. Deliberately not applied to a boss-unique
    /// Warden (see <see cref="ChronoTravelers.Core.Time.TimelineContentFactory.Warden"/>)
    /// or a transient NPC "grind" opponent that's fought and discarded
    /// within one tick, never placed on the map.
    /// </summary>
    public void Enumerate(int number)
    {
        if (_enumerated)
        {
            return;
        }

        var n = ((number % 1000) + 1000) % 1000;
        Name = $"{BaseName}-{n:D3}";
        _enumerated = true;
    }

    public bool HasTag(string tag) => Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);

    /// <summary>Builds a monster from <see cref="MonsterScaling"/>'s tier baselines instead of specifying stats directly.</summary>
    public static Monster Create(string name, int tier, IReadOnlyList<LootTableEntry>? lootTable = null, IReadOnlyList<string>? tags = null, bool isApex = false) =>
        new(name, tier,
            maxHp: MonsterScaling.BaseHp(tier),
            attackPower: MonsterScaling.BaseAttackPower(tier),
            defense: MonsterScaling.BaseDefense(tier),
            speed: MonsterScaling.BaseSpeed(tier),
            xpReward: MonsterScaling.XpReward(tier),
            lootTable: lootTable,
            tags: tags,
            isApex: isApex);

    /// <summary>Places the monster at a grid position — e.g. when its year's population is seeded.</summary>
    public void PlaceAt(Coordinate coordinate) => Position = coordinate;

    /// <summary>Moves the monster to an adjacent coordinate. Legality is the caller's job (see <see cref="World.LevelMap.TryMove"/>).</summary>
    public void MoveTo(Coordinate coordinate) => Position = coordinate;

    public void AddToInventory(Item item) => _inventory.Add(item);

    public bool RemoveFromInventory(Item item)
    {
        if (ReferenceEquals(item, _equippedWeapon))
        {
            _equippedWeapon = null;
        }

        return _inventory.Remove(item);
    }

    /// <summary>
    /// Heals by spending Tachyons — the same rules as
    /// <see cref="ChronoTravelers.Core.Characters.Traveler.Heal"/>: HP restored at
    /// <see cref="TachyonEconomy.HpPerTachyonHealed"/> per Tachyon, capped by both
    /// missing HP and available Tachyons, never overhealing or overspending.
    /// Returns HP restored (0 if full or out of Tachyons — no Tachyons spent then).
    /// </summary>
    public int Heal()
    {
        var missingHp = Health.Max - Health.Current;
        if (missingHp <= 0)
        {
            return 0;
        }

        var ionsNeeded = (int)Math.Ceiling(missingHp / (double)TachyonEconomy.HpPerTachyonHealed);
        var ionsToSpend = Math.Min(ionsNeeded, Tachyons.Current);
        if (ionsToSpend <= 0)
        {
            return 0;
        }

        Tachyons.Spend(ionsToSpend);
        return Health.Heal(ionsToSpend * TachyonEconomy.HpPerTachyonHealed);
    }

    /// <summary>Destroys a carried item for Tachyons — same as <see cref="ChronoTravelers.Core.Characters.Traveler.Convert"/>. Returns the Tachyons actually gained (may be less than the item's convert value if the pool would overflow).</summary>
    public int Convert(Item item)
    {
        if (!_inventory.Remove(item))
        {
            throw new InvalidOperationException($"'{item.Name}' is not in {Name}'s inventory.");
        }

        if (ReferenceEquals(item, _equippedWeapon))
        {
            _equippedWeapon = null;
        }

        return Tachyons.Add(item.ConvertValue());
    }

    /// <summary>Passive Tachyon regen — one <see cref="TachyonPool.Add"/> of 1 every <paramref name="ticksPerRegen"/> ticks. Monsters get regen only (no drain — that's a player survival tax). Returns true if an Tachyon was added.</summary>
    public bool AdvanceTachyonRegenTick(int ticksPerRegen)
    {
        if (ticksPerRegen < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ticksPerRegen), ticksPerRegen, "Ticks per regen must be at least 1.");
        }

        _ticksSinceTachyonRegen++;
        if (_ticksSinceTachyonRegen < ticksPerRegen)
        {
            return false;
        }

        _ticksSinceTachyonRegen = 0;
        return Tachyons.Add(1) > 0;
    }
}
