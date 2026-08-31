using Mutants.Core.Classes;
using Mutants.Core.Items;

namespace Mutants.Core.Time;

/// <summary>
/// One item "archetype" in the timeline catalog — everything about an
/// item except its tier, which comes from the year it drops in.
/// <see cref="TimelineContentFactory.ForArchetype"/> turns it into a
/// concrete <see cref="Item"/> whose Value / AttackBonus / DefenseBonus
/// are derived from that year via <see cref="Items.LootScaling"/>.
/// <see cref="ThemeTags"/> is how a species' or era's loot themes select
/// which archetypes can drop. Loaded from <c>item-archetypes.json</c>.
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
    IReadOnlyList<string> ThemeTags)
{
    public bool HasTheme(string tag) => ThemeTags.Contains(tag, StringComparer.OrdinalIgnoreCase);

    public bool SharesThemeWith(IEnumerable<string> tags) =>
        tags.Any(HasTheme);
}
