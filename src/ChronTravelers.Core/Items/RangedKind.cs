namespace ChronTravelers.Core.Items;

/// <summary>
/// What kind of ranged weapon an item is — <see cref="ItemType.Ranged"/>
/// items fire into the adjacent room with <c>point</c> (Wand) or
/// <c>shoot</c> (Bow/Gun). Each carries a finite built-in shot count
/// (<see cref="Item.AmmoCapacity"/> / <see cref="Item.AmmoRemaining"/>);
/// once spent it can only be converted or sold. Guns and Wands pierce
/// armour, Bows don't (see ChronTravelers.Engine.Combat.RangedResolver).
/// </summary>
public enum RangedKind
{
    /// <summary>Not a ranged weapon.</summary>
    None,
    Wand,
    Bow,
    Gun,
}
