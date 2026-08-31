namespace ChronTravelers.Core.Items;

/// <summary>Broad item categories. Weapon/armor/ranged are wieldable; consumables are used, not wielded; junk exists to be converted or sold.</summary>
public enum ItemType
{
    Weapon,
    Armor,
    Consumable,
    Junk,

    /// <summary>A ranged weapon — a Wand (<c>point</c>) or Bow/Gun (<c>shoot</c>) with a finite built-in shot count. See <see cref="RangedKind"/>.</summary>
    Ranged,
}
