using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Items;

namespace ChronoTravelers.Core.Time;

/// <summary>
/// One item "archetype" in the timeline catalog — everything about an
/// item except its tier, which comes from the year it drops in.
/// <see cref="TimelineContentFactory.ForArchetype"/> turns it into a
/// concrete <see cref="Item"/> whose Value / AttackBonus / DefenseBonus
/// are derived from that year via <see cref="Items.LootScaling"/>.
/// <see cref="ThemeTags"/> is how a species' or era's loot themes select
/// which archetypes can drop. Loaded from <c>item-archetypes.json</c>.
///
/// For an equippable (weapon / armour / ranged) the authored knob is
/// <see cref="PowerMultiplier"/>: it scales the tier baseline
/// (<see cref="Items.LootScaling.EquipBonusFor"/>) and <see cref="Rarity"/>
/// is <em>derived</em> from it by the content loader — never hand-set.
/// Consumables and junk carry an authored <see cref="Rarity"/> and leave
/// <see cref="PowerMultiplier"/> at its default.
/// </summary>
public sealed record ItemArchetypeDefinition(
    string Id,
    string Name,
    ItemType Type,
    Rarity Rarity,
    CharacterClass? RestrictedClass,
    ConsumableEffectType Effect,
    double EffectMagnitude,
    int EffectDurationTicks,
    IReadOnlyList<string> ThemeTags,
    RangedKind RangedKind = RangedKind.None,
    int AmmoCapacity = 0,
    RangedEffectType RangedEffect = RangedEffectType.None,
    double PowerMultiplier = 1.0,
    int Range = 1)
{
    /// <summary>A ranged-weapon archetype (Wand / Bow / Gun) — <see cref="TimelineContentFactory.ForArchetype"/> builds it via <see cref="Item.CreateRanged"/> with a full magazine.</summary>
    public bool IsRanged => RangedKind != RangedKind.None;

    /// <summary>Weapon / Armor / Ranged — the types whose power (and hence rarity) comes from <see cref="PowerMultiplier"/>.</summary>
    public bool IsEquippable => Type is ItemType.Weapon or ItemType.Armor || IsRanged;

    public bool HasTheme(string tag) => ThemeTags.Contains(tag, StringComparer.OrdinalIgnoreCase);

    public bool SharesThemeWith(IEnumerable<string> tags) =>
        tags.Any(HasTheme);
}
