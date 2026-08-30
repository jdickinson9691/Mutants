using Mutants.Core.Classes;
using Mutants.Core.Ions;

namespace Mutants.Core.Items;

/// <summary>
/// A lootable item supporting the three original disposition verbs —
/// wield / sell / convert — per docs/GDD.md §5.
/// </summary>
public sealed record Item(
    string Name,
    ItemType Type,
    int Tier,
    Rarity Rarity,
    int Value,
    int AttackBonus = 0,
    int DefenseBonus = 0,
    CharacterClass? RestrictedClass = null)
{
    /// <summary>
    /// Builds an item whose Value, AttackBonus, and DefenseBonus are all
    /// derived from tier + rarity + type per <see cref="LootScaling"/>,
    /// instead of specifying them directly. Weapons roll an AttackBonus,
    /// armor rolls a DefenseBonus; other item types get neither.
    /// </summary>
    public static Item Create(string name, ItemType type, int tier, Rarity rarity, CharacterClass? restrictedClass = null) =>
        new(name, type, tier, rarity,
            Value: LootScaling.ValueFor(tier, rarity),
            AttackBonus: type == ItemType.Weapon ? LootScaling.CombatBonusFor(tier, rarity) : 0,
            DefenseBonus: type == ItemType.Armor ? LootScaling.CombatBonusFor(tier, rarity) : 0,
            RestrictedClass: restrictedClass);

    /// <summary>Ions gained by destroying this item — docs/GDD.md §2.1.</summary>
    public int ConvertValue() => IonEconomy.ConvertValue(Value);

    /// <summary>
    /// Riblets gained by selling this item. docs/GDD.md §6 ties real sell
    /// price to the store (level, negotiation) — that store-pricing system
    /// is future work (milestone 5: "Stores and the Riblet economy"). This
    /// flat 1:1-with-Value formula is an original placeholder so the
    /// wield/sell/convert three-way choice is fully usable before stores
    /// exist; expect store code to replace/wrap this.
    /// </summary>
    public int SellValue() => Value;

    /// <summary>
    /// Only Weapon/Armor are wieldable at all. docs/GDD.md §4.3: non-class
    /// gear "works at a penalty rather than being hard-blocked."
    /// </summary>
    public bool IsWieldable => Type is ItemType.Weapon or ItemType.Armor;

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
