using ChronTravelers.Core.Classes;
using ChronTravelers.Core.Ions;

namespace ChronTravelers.Core.Items;

/// <summary>
/// A lootable item supporting the three original disposition verbs —
/// wield / sell / convert — per docs/GDD.md §5, plus a fourth for
/// Consumables: use/eat/drink (see <see cref="ConsumableEffect"/>).
///
/// A ranged weapon (<see cref="ItemType.Ranged"/> / <see cref="RangedKind"/>)
/// carries a finite built-in shot count — <see cref="AmmoCapacity"/> and
/// a live, mutable <see cref="AmmoRemaining"/>. Because that state is
/// per-instance, ranged items get a unique <see cref="InstanceId"/>; every
/// other item leaves it <see cref="Guid.Empty"/> and keeps plain
/// value-equality. Once <see cref="AmmoRemaining"/> hits 0 the weapon is
/// <see cref="IsDepleted"/> — no longer fireable, worth only a fraction
/// (down to 25%) on <see cref="ConvertValue"/> / <see cref="SellValue"/>.
/// </summary>
public sealed record Item(
    string Name,
    ItemType Type,
    int Tier,
    Rarity Rarity,
    int Value,
    int AttackBonus = 0,
    int DefenseBonus = 0,
    CharacterClass? RestrictedClass = null,
    ConsumableEffectType ConsumableEffect = ConsumableEffectType.None,
    double EffectMagnitude = 0,
    int EffectDurationTicks = 0,
    RangedKind RangedKind = RangedKind.None,
    int AmmoCapacity = 0,
    RangedEffectType RangedEffect = RangedEffectType.None,
    Guid InstanceId = default)
{
    /// <summary>Shots left in a ranged weapon (starts at <see cref="AmmoCapacity"/>). Mutable — decremented by ChronTravelers.Engine.Combat.RangedResolver. 0 for every non-ranged item.</summary>
    public int AmmoRemaining { get; set; }

    /// <summary>
    /// Builds an item whose Value, AttackBonus, and DefenseBonus are all
    /// derived from tier + rarity + type per <see cref="LootScaling"/>,
    /// instead of specifying them directly. Weapons roll an AttackBonus,
    /// armor rolls a DefenseBonus; other item types get neither.
    /// <paramref name="consumableEffect"/>/<paramref name="effectMagnitude"/>/
    /// <paramref name="effectDurationTicks"/> are content-authored, not
    /// tier/rarity-derived (see ChronTravelers.Engine.Content.ContentLoader) —
    /// unlike combat stats, a potion's strength doesn't follow a formula.
    /// </summary>
    public static Item Create(
        string name, ItemType type, int tier, Rarity rarity, CharacterClass? restrictedClass = null,
        ConsumableEffectType consumableEffect = ConsumableEffectType.None, double effectMagnitude = 0, int effectDurationTicks = 0) =>
        new(name, type, tier, rarity,
            Value: LootScaling.ValueFor(tier, rarity),
            AttackBonus: type == ItemType.Weapon ? LootScaling.CombatBonusFor(tier, rarity) : 0,
            DefenseBonus: type == ItemType.Armor ? LootScaling.CombatBonusFor(tier, rarity) : 0,
            RestrictedClass: restrictedClass,
            ConsumableEffect: consumableEffect,
            EffectMagnitude: effectMagnitude,
            EffectDurationTicks: effectDurationTicks);

    /// <summary>
    /// Builds a ranged weapon — <see cref="ItemType.Ranged"/> with a
    /// unique <see cref="InstanceId"/> and a full magazine
    /// (<see cref="AmmoRemaining"/> = <paramref name="ammoCapacity"/>).
    /// <paramref name="magnitude"/> is the damage multiplier on top of the
    /// weapon's AttackBonus (and the amount of a <see cref="RangedEffectType.Weaken"/>).
    /// </summary>
    public static Item CreateRanged(
        string name, int tier, Rarity rarity, RangedKind kind, int ammoCapacity,
        RangedEffectType rangedEffect = RangedEffectType.None, double magnitude = 1.0,
        CharacterClass? restrictedClass = null, double? powerMultiplier = null)
    {
        // When a power multiplier is given (the content path) it drives
        // both the AttackBonus curve and the rarity band; otherwise fall
        // back to the rarity-keyed shim (tests / sandbox fixtures).
        var attackBonus = powerMultiplier is { } mult
            ? LootScaling.EquipBonusFor(tier, mult)
            : LootScaling.CombatBonusFor(tier, rarity);

        var item = new Item(name, ItemType.Ranged, tier, rarity,
            Value: LootScaling.ValueFor(tier, rarity),
            AttackBonus: attackBonus,
            RestrictedClass: restrictedClass,
            EffectMagnitude: magnitude,
            RangedKind: kind,
            AmmoCapacity: ammoCapacity,
            RangedEffect: rangedEffect,
            InstanceId: Guid.NewGuid());
        item.AmmoRemaining = ammoCapacity;
        return item;
    }

    /// <summary>True for a Consumable that actually does something when used — see ChronTravelers.Core.Characters.Mutant.Consume. A Consumable with no effect data is flavor-only (still sellable/convertible, but "use" refuses it).</summary>
    public bool IsUsable => Type == ItemType.Consumable && ConsumableEffect != ConsumableEffectType.None;

    /// <summary>A ranged weapon (Wand / Bow / Gun).</summary>
    public bool IsRanged => RangedKind != RangedKind.None;

    /// <summary>A ranged weapon that has fired all its shots — no longer usable, only convertible/sellable.</summary>
    public bool IsDepleted => IsRanged && AmmoRemaining <= 0;

    /// <summary>
    /// For a ranged weapon, how much of <see cref="Value"/> is left given
    /// the ammo spent: full when the magazine is full, down to 25% when
    /// empty. 1.0 for every other item.
    /// </summary>
    public double ValueFraction =>
        IsRanged && AmmoCapacity > 0
            ? 0.25 + 0.75 * (Math.Clamp(AmmoRemaining, 0, AmmoCapacity) / (double)AmmoCapacity)
            : 1.0;

    private int EffectiveValue => Math.Max(1, (int)Math.Round(Value * ValueFraction));

    /// <summary>Ions gained by destroying this item — docs/GDD.md §2.1. A partly/fully spent ranged weapon is worth less (see <see cref="ValueFraction"/>).</summary>
    public int ConvertValue() => IonEconomy.ConvertValue(EffectiveValue);

    /// <summary>
    /// Riblets gained by selling this item. docs/GDD.md §6 ties real sell
    /// price to the store (level, negotiation) — that store-pricing system
    /// is future work. This flat 1:1-with-Value placeholder is scaled by
    /// <see cref="ValueFraction"/> for spent ranged weapons.
    /// </summary>
    public int SellValue() => EffectiveValue;

    /// <summary>
    /// Weapon / Armor / Ranged are wieldable. docs/GDD.md §4.3: non-class
    /// gear "works at a penalty rather than being hard-blocked."
    /// </summary>
    public bool IsWieldable => Type is ItemType.Weapon or ItemType.Armor or ItemType.Ranged;

    /// <summary>True if <paramref name="wielder"/> can equip this at full effectiveness.</summary>
    public bool IsClassCompatible(CharacterClass wielder) =>
        RestrictedClass is null || RestrictedClass == wielder;

    /// <summary>
    /// Effectiveness multiplier when wielded by <paramref name="wielder"/>:
    /// 1.0 for class-compatible gear, otherwise an off-class penalty.
    /// The penalty value (not the "penalty, not a hard block" rule itself,
    /// which is GDD-sourced) is original tuning.
    /// </summary>
    public double WieldEffectiveness(CharacterClass wielder) =>
        IsClassCompatible(wielder) ? 1.0 : 0.5;
}
