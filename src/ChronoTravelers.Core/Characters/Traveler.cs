using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Tachyons;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Stats;
using ChronoTravelers.Core.Time;
using ChronoTravelers.Core.World;

namespace ChronoTravelers.Core.Characters;

/// <summary>
/// A Traveler character — human player or NPC, both built on this exact same
/// type per docs/GDD.md §7 ("built on the exact same character/inventory/
/// ability code path" as the requirement that NPCs "play like players").
/// </summary>
public sealed class Traveler
{
    public string Name { get; }
    public CharacterClass Class { get; }
    public ClassDefinition ClassDefinition { get; }

    public int Level { get; private set; }
    public int Xp { get; private set; }
    public StatBlock Stats { get; private set; }
    public HealthPool Health { get; }
    public TachyonPool Tachyons { get; }

    /// <summary>The furthest-future year this Traveler has ever reached — drives the soft level cap (<see cref="TimeScale.SoftLevelCapForYear"/>). Only ever climbs.</summary>
    public int FurthestYearReached { get; private set; } = TimeScale.MinYear;

    /// <summary>Which year this Traveler is currently standing in — docs/GDD.md §3.2. Between <see cref="TimeScale.MinYear"/> and <see cref="TimeScale.MaxYear"/>.</summary>
    public int CurrentYear { get; private set; } = TimeScale.MinYear;

    /// <summary>Current grid position on whichever <see cref="LevelMap"/> this Traveler is on — docs/GDD.md §3.1.</summary>
    public Coordinate Position { get; private set; } = Coordinate.Origin;

    /// <summary>The Warden years this Traveler has already cleared — see <see cref="HasDefeatedWarden"/>.</summary>
    private readonly HashSet<int> _defeatedWardens = [];

    /// <summary>The set of Warden years this Traveler has cleared. Read-only.</summary>
    public IReadOnlyCollection<int> DefeatedWardenYears => _defeatedWardens;

    /// <summary>
    /// Credits on hand — docs/GDD.md §6's store currency. Full store
    /// buy/sell logic is future work (milestone 5); this is just the
    /// balance, needed now for the console status bar (§10).
    /// </summary>
    public int Credits { get; private set; }

    private int _ticksSinceTachyonDrain;
    private int _ticksSinceTachyonRegen;

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

    public Traveler(string name, CharacterClass characterClass, int startingYear = TimeScale.MinYear)
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
        // The player's Tachyon pool has no ceiling (docs/GDD.md §2) — convert
        // loot to stockpile for a long jump; MaxTachyonsAtLevel is only a
        // nominal reference now.
        Tachyons = new TachyonPool(ClassDefinition.MaxTachyonsAtLevel(Level), uncapped: true);
    }

    /// <summary>Reconstructs a Traveler directly from a full state snapshot, bypassing normal gameplay mutation (GainXp/LevelUp/etc.) — for save/load. Inventory (and re-wielding equipped items) is the caller's job afterward via AddToInventory/Wield.</summary>
    private Traveler(
        string name, CharacterClass characterClass, int level, int xp, StatBlock stats,
        int currentHp, int maxHp, int currentTachyons, int maxTachyons, int credits,
        int currentYear, int furthestYearReached, Coordinate position,
        IEnumerable<int> defeatedWardenYears)
    {
        Name = name;
        Class = characterClass;
        ClassDefinition = ClassDefinition.For(characterClass);
        Level = level;
        Xp = xp;
        Stats = stats;
        Health = new HealthPool(maxHp, currentHp);
        Tachyons = new TachyonPool(maxTachyons, currentTachyons, uncapped: true);
        Credits = credits;
        CurrentYear = Math.Clamp(currentYear, TimeScale.MinYear, TimeScale.MaxYear);
        FurthestYearReached = Math.Clamp(furthestYearReached, TimeScale.MinYear, TimeScale.MaxYear);
        Position = position;
        _defeatedWardens = new HashSet<int>(defeatedWardenYears);
    }

    /// <summary>See the private snapshot constructor above — this is its public entry point, used by ChronoTravelers.Engine.Persistence when loading a save.</summary>
    public static Traveler Restore(
        string name, CharacterClass characterClass, int level, int xp, StatBlock stats,
        int currentHp, int maxHp, int currentTachyons, int maxTachyons, int credits,
        int currentYear, int furthestYearReached, Coordinate position,
        IEnumerable<int> defeatedWardenYears)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name cannot be empty.", nameof(name));
        }

        return new Traveler(
            name, characterClass, level, xp, stats,
            currentHp, maxHp, currentTachyons, maxTachyons, credits,
            currentYear, furthestYearReached, position, defeatedWardenYears);
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
    /// Raises Level by 1, grows every stat (the primary by
    /// <see cref="Leveling.PrimaryStatGainPerLevel"/>, the rest by
    /// <see cref="Leveling.SecondaryStatGainPerLevel"/> — docs/GDD.md §4.1),
    /// and recalculates Max HP/Tachyons. Does not enforce the soft cap itself —
    /// callers (GainXp, or explicit debug/testing use) decide whether to
    /// call it.
    /// </summary>
    public void LevelUp()
    {
        Level++;
        Stats = Stats.LevelUp(
            ClassDefinition.PrimaryStat,
            Leveling.PrimaryStatGainPerLevel,
            Leveling.SecondaryStatGainPerLevel);
        Health.SetMax(ClassDefinition.MaxHpAtLevel(Level));
        Tachyons.SetMax(ClassDefinition.MaxTachyonsAtLevel(Level));
    }

    /// <summary>
    /// Moves this Traveler to a different year — docs/GDD.md §3.2. The Tachyon
    /// charge and range check are
    /// ChronoTravelers.Engine.Simulation.TimeTravelResolver's job; this records
    /// where the Traveler now is and advances
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

    /// <summary>Whether this Traveler has already beaten the Warden standing watch over <paramref name="year"/> — docs/GDD.md §3.2. Wardens gate nothing; this just stops the trophy fight repeating.</summary>
    public bool HasDefeatedWarden(int year) => _defeatedWardens.Contains(year);

    /// <summary>Records a Warden-year win, so returning to that year doesn't re-spawn its Warden.</summary>
    public void RecordWardenDefeat(int year) => _defeatedWardens.Add(year);

    /// <summary>
    /// Advances passive Tachyon drain by one world tick — docs/GDD.md §2:
    /// "Survival — passive drain per turn/tick; hitting 0 starts costing
    /// HP." Every <paramref name="ticksPerDrain"/> ticks (see
    /// <see cref="Tachyons.TachyonEconomy.TicksPerTachyonDrain"/>), spends 1 Tachyon, or
    /// deals 1 HP damage instead if none are available. Returns true if
    /// HP was lost this call.
    /// </summary>
    public bool AdvanceTachyonDrainTick(int ticksPerDrain)
    {
        if (ticksPerDrain < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ticksPerDrain), ticksPerDrain, "Ticks per drain must be at least 1.");
        }

        _ticksSinceTachyonDrain++;
        if (_ticksSinceTachyonDrain < ticksPerDrain)
        {
            return false;
        }

        _ticksSinceTachyonDrain = 0;

        if (Tachyons.CanAfford(1))
        {
            Tachyons.Spend(1);
            return false;
        }

        Health.Damage(1);
        return true;
    }

    /// <summary>
    /// Advances passive Tachyon regen by one world tick — the counterpart to
    /// <see cref="AdvanceTachyonDrainTick"/> that keeps the early game
    /// recoverable (playtested). Every <paramref name="ticksPerRegen"/>
    /// ticks (see <see cref="Tachyons.TachyonEconomy.TicksPerTachyonRegen"/>), adds
    /// 1 Tachyon up to the nominal pool max (converting loot can push past it,
    /// passive regen can't). Returns true if an Tachyon was added.
    /// Call it alongside the drain tick; in the present the regen cadence
    /// outpaces the drain so Tachyons net-recover, and in the far future the
    /// drain wins.
    /// </summary>
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
        return Tachyons.Add(1, respectSoftCap: true) > 0;
    }

    /// <summary>
    /// Ticks down every active potion buff by one world tick, dropping any
    /// that just expired — called once per tick alongside
    /// <see cref="AdvanceTachyonDrainTick"/> (see WorldSimulation.Tick), for
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

    /// <summary>Places the Traveler at a specific grid position — e.g. spawning them at a level's start room.</summary>
    public void PlaceAt(Coordinate coordinate) => Position = coordinate;

    /// <summary>Moves the Traveler to an adjacent coordinate. Legality (is there really an exit there?) is the caller's job — see <see cref="LevelMap.TryMove"/>.</summary>
    public void MoveTo(Coordinate coordinate) => Position = coordinate;

    public void AddCredits(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount cannot be negative.");
        }

        Credits += amount;
    }

    public void SpendCredits(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount cannot be negative.");
        }

        if (amount > Credits)
        {
            throw new InvalidOperationException($"Cannot spend {amount} Credits with only {Credits} available.");
        }

        Credits -= amount;
    }

    public void AddToInventory(Item item) => _inventory.Add(item);

    /// <summary>
    /// Destroys an item from inventory for Tachyons — docs/GDD.md §2/§5.
    /// Returns the number of Tachyons gained (the item's full convert value —
    /// the player's pool is uncapped, so nothing overflows). Unequips the
    /// item first if it was wielded.
    /// </summary>
    public int Convert(Item item)
    {
        RemoveFromInventoryOrThrow(item);
        return Tachyons.Add(item.ConvertValue());
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
    /// <remarks>
    /// Overload for anything that isn't a choose-on-drink Meridian Serum
    /// (see <see cref="Item.NeedsStatChoice"/>); calling this on one throws
    /// rather than silently picking a stat for the player.
    /// </remarks>
    public int Consume(Item item) => Consume(item, chosenStat: null);

    /// <summary>
    /// <see cref="Consume(Item)"/>, plus <paramref name="chosenStat"/> for a
    /// choose-on-drink Meridian Serum (<see cref="Item.NeedsStatChoice"/>) —
    /// the stat the player picked when prompted, applied exactly like the
    /// fixed-stat Boost&lt;Stat&gt; effects always have been. Required (and
    /// only meaningful) when <see cref="Item.NeedsStatChoice"/> is true;
    /// ignored for every other item.
    /// </summary>
    public int Consume(Item item, PrimaryStat? chosenStat)
    {
        if (!item.IsUsable)
        {
            throw new InvalidOperationException($"'{item.Name}' cannot be used.");
        }

        if (item.NeedsStatChoice && chosenStat is null)
        {
            throw new InvalidOperationException($"'{item.Name}' needs a stat to boost — ask the player which one before calling Consume.");
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

            case ConsumableEffectType.BoostStrength:
            case ConsumableEffectType.BoostAgility:
            case ConsumableEffectType.BoostResolve:
            case ConsumableEffectType.BoostIntellect:
                // Permanent — rewrites the StatBlock like a level-up. Derived
                // values (attack from the primary stat, defense/speed from
                // Agility) pick it up live; HP isn't stat-derived so it's
                // unchanged. Saved with the rest of Stats, no extra plumbing.
                var boosted = item.ConsumableEffect switch
                {
                    ConsumableEffectType.BoostStrength => PrimaryStat.Strength,
                    ConsumableEffectType.BoostAgility => PrimaryStat.Agility,
                    ConsumableEffectType.BoostResolve => PrimaryStat.Resolve,
                    _ => PrimaryStat.Intellect,
                };
                Stats = Stats.Increase(boosted, (int)Math.Round(item.EffectMagnitude));
                return 0;

            case ConsumableEffectType.BoostChosenStat:
                // chosenStat is guaranteed non-null here by the guard above.
                Stats = Stats.Increase(chosenStat!.Value, (int)Math.Round(item.EffectMagnitude));
                return 0;

            default:
                return 0;
        }
    }

    /// <summary>
    /// Heals by spending Tachyons — docs/GDD.md §2 [SOURCE]: "spend Tachyons to
    /// heal wounds directly," usable at any time (not gated to combat or
    /// a location, unlike Convert/Sell which need an inventory item or a
    /// store). Heals as much as both missing HP and available Tachyons allow
    /// at <see cref="TachyonEconomy.HpPerTachyonHealed"/> per Tachyon — never
    /// overheals past max HP, never spends more Tachyons than it needs to.
    /// Returns the HP actually restored (0 if already at full health or
    /// out of Tachyons, in which case no Tachyons are spent).
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

    /// <summary>
    /// Sells an item from inventory for Credits — docs/GDD.md §5/§6.
    /// Unequips the item first if it was wielded. Pass
    /// <paramref name="credits"/> for a store-negotiated price (see
    /// ChronoTravelers.Core.Economy.Store.BuyFromTraveler); omitted, it falls back
    /// to <see cref="Item.SellValue"/>'s flat rate.
    /// </summary>
    public int Sell(Item item, int? credits = null)
    {
        RemoveFromInventoryOrThrow(item);
        var amount = credits ?? item.SellValue();
        AddCredits(amount);
        return amount;
    }

    /// <summary>
    /// Removes an item from inventory with no payout — e.g. depositing it
    /// into a store the Traveler owns (ChronoTravelers.Core.Economy.Store.Deposit).
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
