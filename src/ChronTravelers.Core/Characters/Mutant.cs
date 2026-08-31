using ChronTravelers.Core.Classes;
using ChronTravelers.Core.Ions;
using ChronTravelers.Core.Items;
using ChronTravelers.Core.Stats;
using ChronTravelers.Core.Time;
using ChronTravelers.Core.World;

namespace ChronTravelers.Core.Characters;

/// <summary>
/// A Mutant character — human player or NPC, both built on this exact same
/// type per docs/GDD.md §7 ("built on the exact same character/inventory/
/// ability code path" as the requirement that NPCs "play like players").
/// </summary>
public sealed class Mutant
{
    public string Name { get; }
    public CharacterClass Class { get; }
    public ClassDefinition ClassDefinition { get; }

    public int Level { get; private set; }
    public int Xp { get; private set; }
    public StatBlock Stats { get; private set; }
    public HealthPool Health { get; }
    public IonPool Ions { get; }

    /// <summary>The furthest-future year this Mutant has ever reached — drives the soft level cap (<see cref="TimeScale.SoftLevelCapForYear"/>). Only ever climbs.</summary>
    public int FurthestYearReached { get; private set; } = TimeScale.MinYear;

    /// <summary>Which year this Mutant is currently standing in — docs/GDD.md §3.2. Between <see cref="TimeScale.MinYear"/> and <see cref="TimeScale.MaxYear"/>.</summary>
    public int CurrentYear { get; private set; } = TimeScale.MinYear;

    /// <summary>Current grid position on whichever <see cref="LevelMap"/> this Mutant is on — docs/GDD.md §3.1.</summary>
    public Coordinate Position { get; private set; } = Coordinate.Origin;

    /// <summary>The Gatekeeper years this Mutant has already cleared — see <see cref="HasDefeatedGatekeeper"/>.</summary>
    private readonly HashSet<int> _defeatedGatekeepers = [];

    /// <summary>The set of Gatekeeper years this Mutant has cleared. Read-only.</summary>
    public IReadOnlyCollection<int> DefeatedGatekeeperYears => _defeatedGatekeepers;

    /// <summary>
    /// Riblets on hand — docs/GDD.md §6's store currency. Full store
    /// buy/sell logic is future work (milestone 5); this is just the
    /// balance, needed now for the console status bar (§10).
    /// </summary>
    public int Riblets { get; private set; }

    private int _ticksSinceIonDrain;
    private int _ticksSinceIonRegen;

    /// <summary>Temporary stat buffs from consumed potions — see <see cref="Consume"/> and <see cref="AdvanceEffectTicks"/>.</summary>
    private readonly List<ActiveEffect> _activeEffects = [];
    public IReadOnlyList<ActiveEffect> ActiveEffects => _activeEffects;

    private readonly List<Item> _inventory = [];
    public IReadOnlyList<Item> Inventory => _inventory;

    public Item? EquippedWeapon { get; private set; }
    public Item? EquippedArmor { get; private set; }

    /// <summary>The wielded ranged weapon (Wand / Bow / Gun), fired with <c>point</c> / <c>shoot</c>. Separate from <see cref="EquippedWeapon"/> — you carry a melee weapon and a ranged sidearm.</summary>
    public Item? EquippedRanged { get; private set; }

    /// <summary>
    /// Turn order / "who acts first" stat for combat — original design
    /// (not GDD-specified), currently just the raw Agility stat.
    /// </summary>
    public int Speed => Stats.Agility;

    /// <summary>Sum of any active BuffAttack potions' magnitude — see <see cref="Consume"/>.</summary>
    private int TemporaryAttackBonus => (int)Math.Round(_activeEffects.Where(e => e.Type == ConsumableEffectType.BuffAttack).Sum(e => e.Magnitude));

    /// <summary>Sum of any active BuffDefense potions' magnitude — see <see cref="Consume"/>.</summary>
    private int TemporaryDefenseBonus => (int)Math.Round(_activeEffects.Where(e => e.Type == ConsumableEffectType.BuffDefense).Sum(e => e.Magnitude));

    /// <summary>
    /// Combat attack power: primary stat + equipped weapon's AttackBonus,
    /// scaled by <see cref="Item.WieldEffectiveness"/> (so off-class
    /// weapons contribute less, per docs/GDD.md §4.3), plus any active
    /// BuffAttack potion. Original design — the GDD confirms "a primary
    /// attack" per class but not its formula.
    /// </summary>
    public int EffectiveAttackPower
    {
        get
        {
            var basePower = Stats.Get(ClassDefinition.PrimaryStat);
            var weaponBonus = EquippedWeapon is null
                ? 0
                : (int)Math.Round(EquippedWeapon.AttackBonus * EquippedWeapon.WieldEffectiveness(Class));

            return basePower + weaponBonus + TemporaryAttackBonus;
        }
    }

    /// <summary>Combat defense: half of Agility + equipped armor's DefenseBonus (scaled by wield effectiveness) + any active BuffDefense potion. Original design.</summary>
    public int EffectiveDefense
    {
        get
        {
            var baseDefense = Stats.Agility / 2;
            var armorBonus = EquippedArmor is null
                ? 0
                : (int)Math.Round(EquippedArmor.DefenseBonus * EquippedArmor.WieldEffectiveness(Class));

            return baseDefense + armorBonus + TemporaryDefenseBonus;
        }
    }

    public Mutant(string name, CharacterClass characterClass, int startingYear = TimeScale.MinYear)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name cannot be empty.", nameof(name));
        }

        if (!TimeScale.IsValidYear(startingYear))
        {
            throw new ArgumentOutOfRangeException(nameof(startingYear), startingYear, $"Year must be between {TimeScale.MinYear} and {TimeScale.MaxYear}.");
        }

        Name = name;
        Class = characterClass;
        ClassDefinition = ClassDefinition.For(characterClass);
        Level = 1;
        Xp = 0;
        Stats = ClassDefinition.BaseStats;
        CurrentYear = startingYear;
        FurthestYearReached = startingYear;
        Health = new HealthPool(ClassDefinition.MaxHpAtLevel(Level));
        Ions = new IonPool(ClassDefinition.MaxIonsAtLevel(Level));
    }

    /// <summary>Reconstructs a Mutant directly from a full state snapshot, bypassing normal gameplay mutation (GainXp/LevelUp/etc.) — for save/load. Inventory (and re-wielding equipped items) is the caller's job afterward via AddToInventory/Wield.</summary>
    private Mutant(
        string name, CharacterClass characterClass, int level, int xp, StatBlock stats,
        int currentHp, int maxHp, int currentIons, int maxIons, int riblets,
        int currentYear, int furthestYearReached, Coordinate position,
        IEnumerable<int> defeatedGatekeeperYears)
    {
        Name = name;
        Class = characterClass;
        ClassDefinition = ClassDefinition.For(characterClass);
        Level = level;
        Xp = xp;
        Stats = stats;
        Health = new HealthPool(maxHp, currentHp);
        Ions = new IonPool(maxIons, currentIons);
        Riblets = riblets;
        CurrentYear = Math.Clamp(currentYear, TimeScale.MinYear, TimeScale.MaxYear);
        FurthestYearReached = Math.Clamp(furthestYearReached, TimeScale.MinYear, TimeScale.MaxYear);
        Position = position;
        _defeatedGatekeepers = new HashSet<int>(defeatedGatekeeperYears);
    }

    /// <summary>See the private snapshot constructor above — this is its public entry point, used by ChronTravelers.Engine.Persistence when loading a save.</summary>
    public static Mutant Restore(
        string name, CharacterClass characterClass, int level, int xp, StatBlock stats,
        int currentHp, int maxHp, int currentIons, int maxIons, int riblets,
        int currentYear, int furthestYearReached, Coordinate position,
        IEnumerable<int> defeatedGatekeeperYears)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name cannot be empty.", nameof(name));
        }

        return new Mutant(
            name, characterClass, level, xp, stats,
            currentHp, maxHp, currentIons, maxIons, riblets,
            currentYear, furthestYearReached, position, defeatedGatekeeperYears);
    }

    /// <summary>
    /// Awards XP and applies as many level-ups as the new total supports,
    /// capped by <see cref="Leveling.SoftLevelCap"/> for the current
    /// <see cref="UnlockedTimeLevel"/>. Returns the number of levels gained.
    /// </summary>
    public int GainXp(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "XP cannot be negative.");
        }

        Xp += amount;
        var levelsGained = 0;
        var cap = TimeScale.SoftLevelCapForYear(FurthestYearReached);

        while (Level < cap && Xp >= Leveling.CumulativeXpForLevel(Level + 1))
        {
            LevelUp();
            levelsGained++;
        }

        return levelsGained;
    }

    /// <summary>
    /// Raises Level by 1, grows the primary stat, and recalculates
    /// Max HP/Ions per docs/GDD.md §4.1 ("Every level grants a stat
    /// increase"). Does not enforce the soft cap itself — callers
    /// (GainXp, or explicit debug/testing use) decide whether to call it.
    /// </summary>
    public void LevelUp()
    {
        Level++;
        Stats = Stats.Increase(ClassDefinition.PrimaryStat);
        Health.SetMax(ClassDefinition.MaxHpAtLevel(Level));
        Ions.SetMax(ClassDefinition.MaxIonsAtLevel(Level));
    }

    /// <summary>
    /// Moves this Mutant to a different year — docs/GDD.md §3.2. The Ion
    /// charge and range check are
    /// ChronTravelers.Engine.Simulation.TimeTravelResolver's job; this records
    /// where the Mutant now is and advances
    /// <see cref="FurthestYearReached"/> if this is new ground. The year
    /// is clamped to the timeline defensively.
    /// </summary>
    public void SetCurrentYear(int year)
    {
        CurrentYear = Math.Clamp(year, TimeScale.MinYear, TimeScale.MaxYear);
        if (CurrentYear > FurthestYearReached)
        {
            FurthestYearReached = CurrentYear;
        }
    }

    /// <summary>Whether this Mutant has already beaten the Gatekeeper standing watch over <paramref name="year"/> — docs/GDD.md §3.2. Gatekeepers gate nothing; this just stops the trophy fight repeating.</summary>
    public bool HasDefeatedGatekeeper(int year) => _defeatedGatekeepers.Contains(year);

    /// <summary>Records a Gatekeeper-year win, so returning to that year doesn't re-spawn its Gatekeeper.</summary>
    public void RecordGatekeeperDefeat(int year) => _defeatedGatekeepers.Add(year);

    /// <summary>
    /// Advances passive Ion drain by one world tick — docs/GDD.md §2:
    /// "Survival — passive drain per turn/tick; hitting 0 starts costing
    /// HP." Every <paramref name="ticksPerDrain"/> ticks (see
    /// <see cref="Ions.IonEconomy.TicksPerIonDrain"/>), spends 1 Ion, or
    /// deals 1 HP damage instead if none are available. Returns true if
    /// HP was lost this call.
    /// </summary>
    public bool AdvanceIonDrainTick(int ticksPerDrain)
    {
        if (ticksPerDrain < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ticksPerDrain), ticksPerDrain, "Ticks per drain must be at least 1.");
        }

        _ticksSinceIonDrain++;
        if (_ticksSinceIonDrain < ticksPerDrain)
        {
            return false;
        }

        _ticksSinceIonDrain = 0;

        if (Ions.CanAfford(1))
        {
            Ions.Spend(1);
            return false;
        }

        Health.Damage(1);
        return true;
    }

    /// <summary>
    /// Advances passive Ion regen by one world tick — the counterpart to
    /// <see cref="AdvanceIonDrainTick"/> that keeps the early game
    /// recoverable (playtested). Every <paramref name="ticksPerRegen"/>
    /// ticks (see <see cref="Ions.IonEconomy.TicksPerIonRegen"/>), adds
    /// 1 Ion (clamped to the pool max). Returns true if an Ion was added.
    /// Call it alongside the drain tick; in the present the regen cadence
    /// outpaces the drain so Ions net-recover, and in the far future the
    /// drain wins.
    /// </summary>
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

    /// <summary>
    /// Ticks down every active potion buff by one world tick, dropping any
    /// that just expired — called once per tick alongside
    /// <see cref="AdvanceIonDrainTick"/> (see WorldSimulation.Tick), for
    /// NPCs and the player alike, regardless of whether either can
    /// currently drink potions themselves.
    /// </summary>
    public void AdvanceEffectTicks()
    {
        if (_activeEffects.Count == 0)
        {
            return;
        }

        for (var i = _activeEffects.Count - 1; i >= 0; i--)
        {
            var remaining = _activeEffects[i].TicksRemaining - 1;
            if (remaining <= 0)
            {
                _activeEffects.RemoveAt(i);
            }
            else
            {
                _activeEffects[i] = _activeEffects[i] with { TicksRemaining = remaining };
            }
        }
    }

    /// <summary>Places the Mutant at a specific grid position — e.g. spawning them at a level's start room.</summary>
    public void PlaceAt(Coordinate coordinate) => Position = coordinate;

    /// <summary>Moves the Mutant to an adjacent coordinate. Legality (is there really an exit there?) is the caller's job — see <see cref="LevelMap.TryMove"/>.</summary>
    public void MoveTo(Coordinate coordinate) => Position = coordinate;

    public void AddRiblets(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount cannot be negative.");
        }

        Riblets += amount;
    }

    public void SpendRiblets(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount cannot be negative.");
        }

        if (amount > Riblets)
        {
            throw new InvalidOperationException($"Cannot spend {amount} Riblets with only {Riblets} available.");
        }

        Riblets -= amount;
    }

    public void AddToInventory(Item item) => _inventory.Add(item);

    /// <summary>
    /// Destroys an item from inventory for Ions — docs/GDD.md §2/§5.
    /// Returns the number of Ions actually gained (may be less than the
    /// item's convert value if it would overflow the Ion pool). Unequips
    /// the item first if it was wielded.
    /// </summary>
    public int Convert(Item item)
    {
        RemoveFromInventoryOrThrow(item);
        return Ions.Add(item.ConvertValue());
    }

    /// <summary>
    /// Uses/eats/drinks a Consumable item from inventory — a fourth
    /// disposition verb alongside wield/sell/convert, for items whose
    /// <see cref="Item.IsUsable"/> is true. The item is consumed either
    /// way once this is called; validating it's actually usable is the
    /// caller's job (see <see cref="Item.IsUsable"/>), same as Wield
    /// leaves <see cref="Item.IsWieldable"/> to its caller. Returns the HP
    /// actually restored for a Heal effect (0 for a buff, which instead
    /// adds a timed <see cref="ActiveEffect"/> — see
    /// <see cref="AdvanceEffectTicks"/> for how those expire).
    /// </summary>
    public int Consume(Item item)
    {
        if (!item.IsUsable)
        {
            throw new InvalidOperationException($"'{item.Name}' cannot be used.");
        }

        RemoveFromInventoryOrThrow(item);

        switch (item.ConsumableEffect)
        {
            case ConsumableEffectType.Heal:
                return Health.Heal((int)Math.Round(item.EffectMagnitude));

            case ConsumableEffectType.BuffAttack:
            case ConsumableEffectType.BuffDefense:
                _activeEffects.Add(new ActiveEffect(item.ConsumableEffect, item.EffectMagnitude, item.EffectDurationTicks));
                return 0;

            default:
                return 0;
        }
    }

    /// <summary>
    /// Heals by spending Ions — docs/GDD.md §2 [SOURCE]: "spend Ions to
    /// heal wounds directly," usable at any time (not gated to combat or
    /// a location, unlike Convert/Sell which need an inventory item or a
    /// store). Heals as much as both missing HP and available Ions allow
    /// at <see cref="IonEconomy.HpPerIonHealed"/> per Ion — never
    /// overheals past max HP, never spends more Ions than it needs to.
    /// Returns the HP actually restored (0 if already at full health or
    /// out of Ions, in which case no Ions are spent).
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

    /// <summary>
    /// Sells an item from inventory for Riblets — docs/GDD.md §5/§6.
    /// Unequips the item first if it was wielded. Pass
    /// <paramref name="riblets"/> for a store-negotiated price (see
    /// ChronTravelers.Core.Economy.Store.BuyFromMutant); omitted, it falls back
    /// to <see cref="Item.SellValue"/>'s flat rate.
    /// </summary>
    public int Sell(Item item, int? riblets = null)
    {
        RemoveFromInventoryOrThrow(item);
        var amount = riblets ?? item.SellValue();
        AddRiblets(amount);
        return amount;
    }

    /// <summary>
    /// Removes an item from inventory with no payout — e.g. depositing it
    /// into a store the Mutant owns (ChronTravelers.Core.Economy.Store.Deposit).
    /// Unequips the item first if it was wielded.
    /// </summary>
    public void RemoveFromInventory(Item item) => RemoveFromInventoryOrThrow(item);

    private void RemoveFromInventoryOrThrow(Item item)
    {
        if (!_inventory.Remove(item))
        {
            throw new InvalidOperationException($"'{item.Name}' is not in {Name}'s inventory.");
        }

        if (EquippedWeapon == item)
        {
            EquippedWeapon = null;
        }

        if (EquippedArmor == item)
        {
            EquippedArmor = null;
        }

        if (EquippedRanged == item)
        {
            EquippedRanged = null;
        }
    }

    /// <summary>
    /// Equips a weapon/armor item from inventory. Off-class gear is
    /// allowed but works at a penalty per docs/GDD.md §4.3 — the caller
    /// (combat system) is expected to read <see cref="Item.WieldEffectiveness"/>
    /// rather than this method blocking the equip.
    /// </summary>
    public void Wield(Item item)
    {
        if (!item.IsWieldable)
        {
            throw new InvalidOperationException($"'{item.Name}' cannot be wielded.");
        }

        if (!_inventory.Contains(item))
        {
            throw new InvalidOperationException($"'{item.Name}' is not in {Name}'s inventory.");
        }

        switch (item.Type)
        {
            case ItemType.Weapon:
                EquippedWeapon = item;
                break;
            case ItemType.Armor:
                EquippedArmor = item;
                break;
            case ItemType.Ranged:
                EquippedRanged = item;
                break;
            default:
                throw new InvalidOperationException($"Unexpected wieldable item type '{item.Type}'.");
        }
    }
}
