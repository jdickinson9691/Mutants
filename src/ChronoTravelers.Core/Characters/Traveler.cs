using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Diagnostics;
using ChronoTravelers.Core.Tachyons;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Monsters;
using ChronoTravelers.Core.Stats;
using ChronoTravelers.Core.Time;
using ChronoTravelers.Core.Traits;
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

    /// <summary>
    /// How many Meridian Serum-style permanent boosts have already landed
    /// on each stat — see <see cref="Consume"/>'s diminishing-returns
    /// calculation. Persisted (<c>ChronoTravelers.Engine.Persistence</c>)
    /// so a save/reload can't reset the falloff and effectively un-diminish
    /// a stat by cycling saves.
    /// </summary>
    private readonly Dictionary<PrimaryStat, int> _elixirUsesByStat = [];

    /// <summary>Read-only view of <see cref="_elixirUsesByStat"/>, for save-data mapping. A stat with no recorded uses is absent, not zero.</summary>
    public IReadOnlyDictionary<PrimaryStat, int> ElixirUsesByStat => _elixirUsesByStat;

    /// <summary>
    /// How much a permanent stat elixir grown from base magnitude
    /// <paramref name="baseMagnitude"/> raises <paramref name="stat"/> the
    /// *next* time one is drunk, and records that use. Each successive
    /// elixir on the same stat is worth <see cref="ElixirDiminishingFalloff"/>
    /// times the last (never below <see cref="ElixirMinimumBoost"/>) — a
    /// deliberate "no hard cap, but stacking stops being worth the grind"
    /// design (docs/GDD.md §4.1): with the base +5 and a 0.75 falloff, the
    /// *first several* drinks still matter (5, 4, 3, 2, 2, 1, 1, …) but the
    /// running total on one stat converges toward roughly +20 rather than
    /// growing without bound the way an unlimited +5-per-drink did — that
    /// unbounded growth (stacking dozens of serums across a long timeline
    /// run) was a real balance bug, see
    /// <see cref="ChronoTravelers.Core.Monsters.MonsterScaling"/>'s doc
    /// comment.
    /// </summary>
    private const double ElixirDiminishingFalloff = 0.75;

    /// <summary>The smallest a diminished elixir boost ever rounds down to — keeps drinking one always worth *something*, matching the "no hard cap" design (see <see cref="ElixirDiminishingFalloff"/>).</summary>
    private const int ElixirMinimumBoost = 1;

    private int NextElixirBoost(PrimaryStat stat, double baseMagnitude)
    {
        var uses = _elixirUsesByStat.GetValueOrDefault(stat);
        var boost = Math.Max(ElixirMinimumBoost, (int)Math.Round(baseMagnitude * Math.Pow(ElixirDiminishingFalloff, uses)));
        _elixirUsesByStat[stat] = uses + 1;
        return boost;
    }

    private readonly List<Item> _inventory = [];
    public IReadOnlyList<Item> Inventory => _inventory;

    /// <summary>
    /// The most items a Traveler's pack can hold at once — human and NPC
    /// alike, per docs/GDD.md §7's "built on the exact same character/
    /// inventory/ability code path." Original tuning (not GDD-specified):
    /// keeps loot management a real decision (wield/sell/convert/stock —
    /// §5) rather than an unbounded pile, and gives NPC store-tending
    /// (<see cref="ChronoTravelers.Engine.Npc.NpcController"/>) something
    /// real to manage. See <see cref="AddToInventory"/>.
    /// </summary>
    public const int MaxInventorySize = 15;

    /// <summary>
    /// This NPC's rolled <see cref="CreatureTraitKind"/> — <c>None</c> until
    /// <see cref="AssignTrait"/> is called. Only
    /// <c>ChronoTravelers.Engine.Npc.NpcPopulation.Create</c> (spawn/respawn)
    /// ever calls it, so the player's own Traveler never carries a trait —
    /// see docs/GDD.md §7. NPCs are re-simulated fresh each session (never
    /// persisted — see <c>ChronoTravelers.Engine.Persistence.CharacterSaveData</c>'s
    /// doc comment), so this needed no save-format change.
    /// </summary>
    public CreatureTraitKind Trait { get; private set; } = CreatureTraitKind.None;

    private bool _traitAssigned;

    /// <summary>Assigns this NPC's <see cref="Trait"/> once — a no-op past the first call (mirrors <see cref="Monsters.Monster.AssignTrait"/>'s guard). A legitimate roll of <c>CreatureTraitKind.None</c> still counts as "assigned" so it can't be silently overwritten by a stray second call.</summary>
    public void AssignTrait(CreatureTraitKind trait)
    {
        if (_traitAssigned)
        {
            return;
        }

        Trait = trait;
        _traitAssigned = true;
    }

    public Item? EquippedWeapon { get; private set; }
    public Item? EquippedArmor { get; private set; }

    /// <summary>The wielded ranged weapon (Wand / Bow / Gun), fired with <c>point</c> / <c>shoot</c>. Separate from <see cref="EquippedWeapon"/> — you carry a melee weapon and a ranged sidearm.</summary>
    public Item? EquippedRanged { get; private set; }

    /// <summary>
    /// The monster locked in as a ranged target via <c>fight</c> while a
    /// ranged weapon is readied — <c>fight</c> marks it instead of opening
    /// blocking melee, and <c>fire &lt;direction&gt;</c> only ever shoots
    /// this monster specifically (see ChronoTravelers.Engine.Combat.RangedResolver
    /// and the console's <c>HandleFight</c>/<c>HandleShoot</c>). Session
    /// state, not saved. Cleared on the target's death, on changing year
    /// (<see cref="SetCurrentYear"/>), or by locking a different target.
    /// </summary>
    public Monster? RangedTarget { get; private set; }

    /// <summary>Locks (or clears, with <c>null</c>) <see cref="RangedTarget"/>.</summary>
    public void SetRangedTarget(Monster? monster) => RangedTarget = monster;

    /// <summary>
    /// Turn order / "who acts first" stat for combat — original design
    /// (not GDD-specified), the raw Agility stat plus any active BuffSpeed
    /// potion (see <see cref="Consume"/>) plus any flat passive Speed bonus
    /// (Spy "Quick Reflexes" / Engineer "Overclocked Reflexes" — docs/GDD.md §4.2.1).
    /// </summary>
    public int Speed => Stats.Agility + TemporarySpeedBonus + (int)PassiveTraits.Sum(Class, Level, PassiveHook.FlatSpeedBonus);

    /// <summary>Sum of any active BuffAttack potions' magnitude — see <see cref="Consume"/>.</summary>
    private int TemporaryAttackBonus => (int)Math.Round(_activeEffects.Where(e => e.Type == ConsumableEffectType.BuffAttack).Sum(e => e.Magnitude));

    /// <summary>Sum of any active BuffDefense potions' magnitude — see <see cref="Consume"/>.</summary>
    private int TemporaryDefenseBonus => (int)Math.Round(_activeEffects.Where(e => e.Type == ConsumableEffectType.BuffDefense).Sum(e => e.Magnitude));

    /// <summary>Sum of any active BuffSpeed potions' magnitude — see <see cref="Consume"/>.</summary>
    private int TemporarySpeedBonus => (int)Math.Round(_activeEffects.Where(e => e.Type == ConsumableEffectType.BuffSpeed).Sum(e => e.Magnitude));

    /// <summary>
    /// Combat attack power: primary stat + equipped weapon's AttackBonus,
    /// scaled by <see cref="Item.WieldEffectiveness"/> (so off-class
    /// weapons contribute less, per docs/GDD.md §4.3), plus any active
    /// BuffAttack potion. Original design — the GDD confirms "a primary
    /// attack" per class but not its formula.
    /// </summary>
    /// <summary>Pack Leader trait (NPC-side hook — see <see cref="Traits.CreatureTraitKind.PackLeader"/>'s doc comment): a flat EffectiveAttackPower bonus, the NPC-flavored translation of the monster-side "rallies the room" mechanic — an NPC isn't a spatial pack animal, but it still fights like it's leading one.</summary>
    private const double PackLeaderAttackBonusPct = 0.15;

    public int EffectiveAttackPower
    {
        get
        {
            var basePower = Stats.Get(ClassDefinition.PrimaryStat);
            var weaponBonus = EquippedWeapon is null
                ? 0
                : (int)Math.Round(EquippedWeapon.AttackBonus * EquippedWeapon.WieldEffectiveness(Class, OffClassPenaltyReduction));

            var passiveBonus = basePower + weaponBonus + TemporaryAttackBonus;

            if (Trait == CreatureTraitKind.PackLeader)
            {
                passiveBonus += (int)Math.Round(passiveBonus * PackLeaderAttackBonusPct);
            }

            // Scientist "Overcurrent" — bonus while flush with Tachyons
            // (docs/GDD.md §4.2.1). Read against the nominal pool max, not
            // Current alone, since the player's pool is uncapped.
            if (Tachyons.Max > 0 && Tachyons.Current >= Tachyons.Max * 0.5)
            {
                var overcurrentBonus = (int)Math.Round(passiveBonus * PassiveTraits.Sum(Class, Level, PassiveHook.HighTachyonAttackBonusPct));
                passiveBonus += overcurrentBonus;
                PassiveActivationTracker.Record(Class, PassiveHook.HighTachyonAttackBonusPct, overcurrentBonus);
            }

            // Soldier "Juggernaut Momentum" — grows with consecutive rounds
            // landed this fight; see RecordAttackLanded/ResetPerFightState.
            var juggernautBonus = (int)Math.Round(passiveBonus * _consecutiveHitStacks * PassiveTraits.Sum(Class, Level, PassiveHook.ConsecutiveHitAttackBonusPct));
            passiveBonus += juggernautBonus;
            if (_consecutiveHitStacks > 0)
            {
                PassiveActivationTracker.Record(Class, PassiveHook.ConsecutiveHitAttackBonusPct, juggernautBonus);
            }

            return passiveBonus;
        }
    }

    /// <summary>Off-class wield-penalty reduction from unlocked passives (Soldier "Weapon Discipline" / Engineer "Field-Tested Gear") — see <see cref="Items.Item.WieldEffectiveness"/>.</summary>
    private double OffClassPenaltyReduction => PassiveTraits.Sum(Class, Level, PassiveHook.OffClassPenaltyReductionPct);

    /// <summary>
    /// How many consecutive rounds of the current fight this Traveler has
    /// landed an attack — Soldier's "Juggernaut Momentum" passive
    /// (docs/GDD.md §4.2.1). Capped at 10 stacks (matching the passive's
    /// own description) since there's no "miss" in this combat model to
    /// naturally break a streak — see <see cref="RecordAttackLanded"/>.
    /// </summary>
    private int _consecutiveHitStacks;

    private const int MaxConsecutiveHitStacks = 10;

    /// <summary>Call once per attack this Traveler lands — advances Juggernaut Momentum's stack (see <see cref="_consecutiveHitStacks"/>). A no-op for every class without that passive.</summary>
    public void RecordAttackLanded() => _consecutiveHitStacks = Math.Min(MaxConsecutiveHitStacks, _consecutiveHitStacks + 1);

    /// <summary>Resets fight-scoped passive state (Juggernaut Momentum's streak, Unbreakable's once-per-fight charge) — call at the start of every fight.</summary>
    public void ResetPerFightState()
    {
        _consecutiveHitStacks = 0;
        _deathProofUsedThisFight = false;
    }

    /// <summary>
    /// Damage multiplier for an attack this Traveler makes against
    /// <paramref name="target"/> — folds in the passives that depend on
    /// knowing the target (Spy "Opportunist" vs. a low-HP target, Scientist
    /// "Field Calibration" vs. a Caster-archetype monster), on top of
    /// whatever multiplier the caller already has (an ability's own
    /// Magnitude, say). Multiplicative with the caller's multiplier, not
    /// additive with each other's percentages, to keep this simple.
    /// </summary>
    /// <summary>Ambusher trait (NPC-side hook — see <see cref="Traits.CreatureTraitKind.Ambusher"/>'s doc comment): the opening-strike bonus against a still-undamaged target, the NPC-flavored translation of the monster-side ambush-hit bonus — a grind fight has no spatial ambush to land, but a fresh (full-HP) target is the closest equivalent to "caught it by surprise."</summary>
    private const double AmbusherFreshTargetBonusPct = 0.20;

    public double AttackDamageMultiplierAgainst(Monster target)
    {
        var multiplier = 1.0;

        if (target.Health.Max > 0 && target.Health.Current <= target.Health.Max * 0.4)
        {
            var bonus = PassiveTraits.Sum(Class, Level, PassiveHook.LowHpTargetAttackBonusPct);
            multiplier += bonus;
            PassiveActivationTracker.Record(Class, PassiveHook.LowHpTargetAttackBonusPct, bonus);
        }

        if (target.HasTag("caster"))
        {
            var bonus = PassiveTraits.Sum(Class, Level, PassiveHook.CasterDamageBonusPct);
            multiplier += bonus;
            PassiveActivationTracker.Record(Class, PassiveHook.CasterDamageBonusPct, bonus);
        }

        if (Trait == CreatureTraitKind.Ambusher && target.Health.Max > 0 && target.Health.Current == target.Health.Max)
        {
            multiplier += AmbusherFreshTargetBonusPct;
        }

        return multiplier;
    }

    /// <summary>
    /// Combat defense: Agility ÷ <see cref="MonsterScaling.AgilityToDefenseDivisor"/>
    /// + equipped armor's DefenseBonus (scaled by wield effectiveness) +
    /// any active BuffDefense potion. Original design; the divisor lives
    /// on <see cref="MonsterScaling"/> (not here) since that's the single
    /// source of truth for keeping player and monster combat stats
    /// proportionate — see its doc comment.
    /// </summary>
    public int EffectiveDefense
    {
        get
        {
            var baseDefense = (int)(Stats.Agility / MonsterScaling.AgilityToDefenseDivisor);
            var armorBonus = EquippedArmor is null
                ? 0
                : (int)Math.Round(EquippedArmor.DefenseBonus * EquippedArmor.WieldEffectiveness(Class, OffClassPenaltyReduction));

            // Soldier "Hardened" — bonus on top of armor's own DefenseBonus contribution (docs/GDD.md §4.2.1).
            armorBonus += (int)Math.Round(armorBonus * PassiveTraits.Sum(Class, Level, PassiveHook.ArmorDefenseBonusPct));

            // Engineer "Improvised Plating" — flat Defense bonus.
            var flatBonus = (int)PassiveTraits.Sum(Class, Level, PassiveHook.FlatDefenseBonus);

            return baseDefense + armorBonus + TemporaryDefenseBonus + flatBonus;
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
        IEnumerable<int> defeatedWardenYears,
        IEnumerable<KeyValuePair<PrimaryStat, int>>? elixirUsesByStat)
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
        if (elixirUsesByStat is not null)
        {
            foreach (var (stat, uses) in elixirUsesByStat)
            {
                _elixirUsesByStat[stat] = uses;
            }
        }
    }

    /// <summary>See the private snapshot constructor above — this is its public entry point, used by ChronoTravelers.Engine.Persistence when loading a save.</summary>
    public static Traveler Restore(
        string name, CharacterClass characterClass, int level, int xp, StatBlock stats,
        int currentHp, int maxHp, int currentTachyons, int maxTachyons, int credits,
        int currentYear, int furthestYearReached, Coordinate position,
        IEnumerable<int> defeatedWardenYears,
        IEnumerable<KeyValuePair<PrimaryStat, int>>? elixirUsesByStat = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name cannot be empty.", nameof(name));
        }

        return new Traveler(
            name, characterClass, level, xp, stats,
            currentHp, maxHp, currentTachyons, maxTachyons, credits,
            currentYear, furthestYearReached, position, defeatedWardenYears, elixirUsesByStat);
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

        RangedTarget = null; // a locked target doesn't survive a jump to a different year
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

        // Scientist "Insulated Coils" — slows drain by stretching the tick
        // interval itself, so the caller (WorldSimulation) never needs to
        // know this passive exists (docs/GDD.md §4.2.1).
        var drainRateReduction = PassiveTraits.Sum(Class, Level, PassiveHook.TachyonDrainRateReductionPct);
        if (drainRateReduction > 0)
        {
            ticksPerDrain = Math.Max(1, (int)Math.Round(ticksPerDrain * (1 + drainRateReduction)));
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

        // Doctor "Overwatch" / Scientist "Efficient Circuits" — faster regen
        // by shrinking the tick interval (the two stack, same convention as
        // AdvanceTachyonDrainTick's reduction above).
        var regenRateBonus = PassiveTraits.Sum(Class, Level, PassiveHook.TachyonRegenRateBonusPct);
        if (regenRateBonus > 0)
        {
            ticksPerRegen = Math.Max(1, (int)Math.Round(ticksPerRegen / (1 + regenRateBonus)));
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
    /// currently drink potions themselves. A <see cref="ConsumableEffectType.HealOverTime"/>
    /// effect also heals for its Magnitude on every tick it's active
    /// (including the tick it expires on) — the one active effect that does
    /// something besides just running out; BuffAttack/BuffDefense/BuffSpeed
    /// only ever change what <see cref="EffectiveAttackPower"/> /
    /// <see cref="EffectiveDefense"/> / <see cref="Speed"/> read while active.
    /// </summary>
    public void AdvanceEffectTicks()
    {
        // Doctor "Vital Reserves" — a trickle of passive HP regen every
        // world tick, independent of whether any potion is active
        // (docs/GDD.md §4.2.1), so this runs before the active-effects
        // early-out below.
        var vitalReservesRate = PassiveTraits.Sum(Class, Level, PassiveHook.MaxHpRegenPerTickPct);
        if (vitalReservesRate > 0 && !Health.IsDead)
        {
            var healed = Health.Heal((int)Math.Round(Health.Max * vitalReservesRate));
            PassiveActivationTracker.Record(Class, PassiveHook.MaxHpRegenPerTickPct, healed);
        }

        if (_activeEffects.Count == 0)
        {
            return;
        }

        for (var i = _activeEffects.Count - 1; i >= 0; i--)
        {
            if (_activeEffects[i].Type == ConsumableEffectType.HealOverTime)
            {
                Health.Heal((int)Math.Round(_activeEffects[i].Magnitude));
            }

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

    /// <summary>Whether this fight's Soldier "Unbreakable" charge has already saved this Traveler once — see <see cref="TakeDamage"/> and <see cref="ResetPerFightState"/>.</summary>
    private bool _deathProofUsedThisFight;

    /// <summary>
    /// Applies incoming damage, routing it through every damage-mitigating
    /// passive (docs/GDD.md §4.2.1) so grind combat (<see cref="Engine.Combat.CombatResolver"/>-
    /// style callers, referenced only in doc comments here to avoid a
    /// Core→Engine dependency), interactive combat, and ambushes all behave
    /// consistently — the single call site every "Traveler takes damage"
    /// path should use instead of <c>Health.Damage</c> directly. Returns the
    /// HP actually lost (as <see cref="Stats.HealthPool.Damage"/> does).
    /// </summary>
    /// <param name="rawAmount">The un-mitigated incoming damage.</param>
    /// <param name="attackerIsEcho">True if the attacker is "echo"-tagged — triggers Doctor's "Resonant Calm".</param>
    /// <param name="isAmbush">True if this damage comes from an ambush (a monster's opening hit before a fight starts) — triggers Soldier's "Thick Hide". Ambush dodge/negate chances are rolled by the caller (they need randomness Core doesn't own — see <see cref="AmbushDodgeChance"/>/<see cref="AmbushNegateChance"/>); by the time this is called, the ambush is assumed to be landing.</param>
    public int TakeDamage(int rawAmount, bool attackerIsEcho = false, bool isAmbush = false)
    {
        if (rawAmount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rawAmount), rawAmount, "Damage cannot be negative.");
        }

        var amount = (double)rawAmount;

        if (Health.Max > 0 && Health.Current <= Health.Max * 0.3)
        {
            var reduction = amount * PassiveTraits.Sum(Class, Level, PassiveHook.LowHpDamageReductionPct);
            amount -= reduction;
            PassiveActivationTracker.Record(Class, PassiveHook.LowHpDamageReductionPct, reduction);
        }

        if (attackerIsEcho)
        {
            var reduction = amount * PassiveTraits.Sum(Class, Level, PassiveHook.EchoDamageReductionPct);
            amount -= reduction;
            PassiveActivationTracker.Record(Class, PassiveHook.EchoDamageReductionPct, reduction);
        }

        if (isAmbush)
        {
            var reduction = amount * PassiveTraits.Sum(Class, Level, PassiveHook.AmbushDamageReductionPct);
            amount -= reduction;
            PassiveActivationTracker.Record(Class, PassiveHook.AmbushDamageReductionPct, reduction);
        }

        var mitigated = Math.Max(0, (int)Math.Round(amount));

        // Soldier "Unbreakable" — once per fight, a killing blow leaves 1 HP instead.
        if (mitigated >= Health.Current && Health.Current > 0 && !_deathProofUsedThisFight
            && PassiveTraits.Any(Class, Level, PassiveHook.DeathProofOncePerFight))
        {
            _deathProofUsedThisFight = true;
            mitigated = Health.Current - 1;
            PassiveActivationTracker.Record(Class, PassiveHook.DeathProofOncePerFight, 1);
        }

        return Health.Damage(mitigated);
    }

    /// <summary>Chance [0,1) an ambush is dodged entirely (Spy "Fleet-Footed" / Engineer "Redundant Systems") — the caller rolls it (see <see cref="TakeDamage"/>'s doc comment for why Core doesn't own randomness).</summary>
    public double AmbushDodgeChance => PassiveTraits.Sum(Class, Level, PassiveHook.AmbushDodgeChancePct);

    /// <summary>Chance [0,1) an ambush is negated entirely (Doctor "Trauma Ward") — the caller rolls it.</summary>
    public double AmbushNegateChance => PassiveTraits.Sum(Class, Level, PassiveHook.AmbushNegateChancePct);

    /// <summary>Multiplier applied to aggro this Traveler causes nearby monsters to gain (Spy "Low Profile") — 1.0 with no passive, down toward 0 as reduction stacks.</summary>
    public double AggroGainMultiplier => Math.Max(0, 1.0 - PassiveTraits.Sum(Class, Level, PassiveHook.AggroGainReductionPct));

    /// <summary>Store discount-when-buying / bonus-when-selling from Spy's "Light Fingers"/"Silent Partner" — see <see cref="Economy.Store"/>.</summary>
    public double StoreDiscountBonus => PassiveTraits.Sum(Class, Level, PassiveHook.StoreDiscountBonusPct);

    /// <summary>Chance [0,1) an ability cast costs no Tachyons at all (Scientist "Stable Core") — the caller rolls it before charging <see cref="EffectiveCastCost"/>.</summary>
    public double FreeCastChance => PassiveTraits.Sum(Class, Level, PassiveHook.FreeCastChancePct);

    /// <summary>
    /// The Tachyon cost actually charged for casting an ability whose
    /// nominal cost is <paramref name="baseCost"/> — halved (Engineer
    /// "Failsafe Capacitor") when paying it in full would drop the pool
    /// below 10% of its nominal max.
    /// </summary>
    public int EffectiveCastCost(int baseCost)
    {
        if (baseCost <= 0)
        {
            return baseCost;
        }

        var discount = PassiveTraits.Sum(Class, Level, PassiveHook.LowTachyonCastDiscountPct);
        if (discount <= 0 || Tachyons.Max <= 0)
        {
            return baseCost;
        }

        var remainingAfterFullCost = Tachyons.Current - baseCost;
        if (remainingAfterFullCost >= Tachyons.Max * 0.1)
        {
            return baseCost;
        }

        var reduced = Math.Max(0, (int)Math.Round(baseCost * (1 - discount)));
        PassiveActivationTracker.Record(Class, PassiveHook.LowTachyonCastDiscountPct, baseCost - reduced);
        return reduced;
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

    /// <summary>
    /// Adds <paramref name="item"/> to the pack. Returns false (and adds
    /// nothing) if the pack is already at <see cref="MaxInventorySize"/> —
    /// callers that need to tell the player/NPC "pack's full" check this;
    /// callers that are certain the pack has room (a fresh character's
    /// starter kit, a controlled test fixture) can ignore it, same as any
    /// other bool-returning "did this work" method in this codebase (e.g.
    /// <see cref="Economy.Store.BuyFromTraveler"/>). <paramref name="enforceCap"/>
    /// is false only for <c>ChronoTravelers.Engine.Persistence.CharacterMapper</c>
    /// restoring a save written before this cap existed — that path must
    /// never silently drop a returning player's items.
    /// </summary>
    public bool AddToInventory(Item item, bool enforceCap = true)
    {
        if (enforceCap && _inventory.Count >= MaxInventorySize)
        {
            return false;
        }

        _inventory.Add(item);
        return true;
    }

    /// <summary>
    /// Scavenger trait (NPC-side hook, mirroring <see cref="Monsters.Monster"/>'s
    /// own Scavenger Convert bonus) — junk is convert-only now (no store
    /// ever buys it), so this is the trait's whole payoff for an NPC that
    /// used to also get a cut from <em>selling</em> junk before that path
    /// was removed. Never rolled for the player (<see cref="Trait"/> stays
    /// <c>None</c>), so this is a no-op there.
    /// </summary>
    private const double ScavengerConvertValueBonusPct = 0.25;

    /// <summary>
    /// Destroys an item from inventory for Tachyons — docs/GDD.md §2/§5.
    /// Returns the number of Tachyons gained (the item's full convert value —
    /// the player's pool is uncapped, so nothing overflows). Unequips the
    /// item first if it was wielded.
    /// </summary>
    public int Convert(Item item)
    {
        RemoveFromInventoryOrThrow(item);
        var convertBonus = PassiveTraits.Sum(Class, Level, PassiveHook.ConvertValueBonusPct);
        var junkBonus = JunkValueBonus(item);
        var scavengerBonus = Trait == CreatureTraitKind.Scavenger ? ScavengerConvertValueBonusPct : 0;
        var bonus = convertBonus + junkBonus + scavengerBonus;
        var value = bonus <= 0 ? item.ConvertValue() : (int)Math.Round(item.ConvertValue() * (1 + bonus));
        PassiveActivationTracker.Record(Class, PassiveHook.ConvertValueBonusPct, (int)Math.Round(item.ConvertValue() * convertBonus));
        PassiveActivationTracker.Record(Class, PassiveHook.JunkValueBonusPct, (int)Math.Round(item.ConvertValue() * junkBonus));
        return Tachyons.Add(value);
    }

    /// <summary>
    /// Engineer "Salvage Sense" (Junk items only — this game's stand-in for
    /// "scrap-themed", since <see cref="Item"/> carries no content theme
    /// tags at runtime; see docs/GDD.md §4.2.1's implementation note), 0
    /// for anything else.
    /// </summary>
    private double JunkValueBonus(Item item) =>
        item.Type == ItemType.Junk ? PassiveTraits.Sum(Class, Level, PassiveHook.JunkValueBonusPct) : 0;

    /// <summary>
    /// Uses/eats/drinks a Consumable item from inventory — a fourth
    /// disposition verb alongside wield/sell/convert, for items whose
    /// <see cref="Item.IsUsable"/> is true. The item is consumed either
    /// way once this is called; validating it's actually usable is the
    /// caller's job (see <see cref="Item.IsUsable"/>), same as Wield
    /// leaves <see cref="Item.IsWieldable"/> to its caller. Returns the HP
    /// actually restored for a Heal effect, the Tachyons actually restored
    /// for a RestoreTachyons effect, and 0 for every timed effect
    /// (BuffAttack/BuffDefense/BuffSpeed/HealOverTime), which instead
    /// adds a timed <see cref="ActiveEffect"/> — see
    /// <see cref="AdvanceEffectTicks"/> for how those expire (and, for
    /// HealOverTime, what they do on every tick).
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
            {
                var healBonus = ConsumableHealBonus;
                var healed = Health.Heal((int)Math.Round(item.EffectMagnitude * (1 + healBonus)));
                PassiveActivationTracker.Record(Class, PassiveHook.ConsumableHealBonusPct, (int)Math.Round(item.EffectMagnitude * healBonus));
                return healed;
            }

            case ConsumableEffectType.HealOverTime:
            {
                // Doctor "Steady Hands" bumps the per-tick heal amount too.
                var healBonus = ConsumableHealBonus;
                _activeEffects.Add(new ActiveEffect(item.ConsumableEffect, item.EffectMagnitude * (1 + healBonus), item.EffectDurationTicks));
                PassiveActivationTracker.Record(Class, PassiveHook.ConsumableHealBonusPct, item.EffectMagnitude * healBonus);
                return 0;
            }

            case ConsumableEffectType.BuffAttack:
            case ConsumableEffectType.BuffDefense:
            case ConsumableEffectType.BuffSpeed:
                _activeEffects.Add(new ActiveEffect(item.ConsumableEffect, item.EffectMagnitude, item.EffectDurationTicks));
                return 0;

            case ConsumableEffectType.RestoreTachyons:
                return Tachyons.Add((int)Math.Round(item.EffectMagnitude));

            case ConsumableEffectType.BoostStrength:
            case ConsumableEffectType.BoostAgility:
            case ConsumableEffectType.BoostResolve:
            case ConsumableEffectType.BoostIntellect:
                // Permanent — rewrites the StatBlock like a level-up. Derived
                // values (attack from the primary stat, defense/speed from
                // Agility) pick it up live; HP isn't stat-derived so it's
                // unchanged. Saved with the rest of Stats, no extra plumbing.
                // The actual amount added diminishes with repeat use on the
                // same stat — see NextElixirBoost.
                var boosted = item.ConsumableEffect switch
                {
                    ConsumableEffectType.BoostStrength => PrimaryStat.Strength,
                    ConsumableEffectType.BoostAgility => PrimaryStat.Agility,
                    ConsumableEffectType.BoostResolve => PrimaryStat.Resolve,
                    _ => PrimaryStat.Intellect,
                };
                Stats = Stats.Increase(boosted, NextElixirBoost(boosted, item.EffectMagnitude));
                return 0;

            case ConsumableEffectType.BoostChosenStat:
                // chosenStat is guaranteed non-null here by the guard above.
                Stats = Stats.Increase(chosenStat!.Value, NextElixirBoost(chosenStat.Value, item.EffectMagnitude));
                return 0;

            default:
                return 0;
        }
    }

    /// <summary>Doctor "Steady Hands" — bonus fraction applied to a consumable's healing (instant or over-time).</summary>
    private double ConsumableHealBonus => PassiveTraits.Sum(Class, Level, PassiveHook.ConsumableHealBonusPct);

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
        var healBonus = PassiveTraits.Sum(Class, Level, PassiveHook.HealRatioBonusPct);
        var hpPerIon = TachyonEconomy.HpPerTachyonHealed * (1 + healBonus);
        var healed = Health.Heal((int)Math.Round(ionsToSpend * hpPerIon));
        PassiveActivationTracker.Record(Class, PassiveHook.HealRatioBonusPct, (int)Math.Round(ionsToSpend * TachyonEconomy.HpPerTachyonHealed * healBonus));
        return healed;
    }

    /// <summary>
    /// Sells an item from inventory for Credits — docs/GDD.md §5/§6.
    /// Unequips the item first if it was wielded. Pass
    /// <paramref name="credits"/> for a store-negotiated price (see
    /// ChronoTravelers.Core.Economy.Store.BuyFromTraveler); omitted, it falls back
    /// to <see cref="Item.SellValue"/>'s flat rate. Throws for a Junk item —
    /// junk is convert-only now (see <see cref="Convert"/>), never sellable
    /// to any store; <see cref="Economy.Store.BuyFromTraveler"/> /
    /// <see cref="Economy.Store.Deposit(Traveler, Item, int)"/> both refuse
    /// it too, so this is a backstop against a caller reaching Sell
    /// directly with one.
    /// </summary>
    public int Sell(Item item, int? credits = null)
    {
        if (item.Type == ItemType.Junk)
        {
            throw new InvalidOperationException($"'{item.Name}' is junk — it can only be converted for Tachyons, not sold.");
        }

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
