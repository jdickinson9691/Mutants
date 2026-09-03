using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Engine.Persistence;

namespace ChronoTravelers.Game;

/// <summary>Shared new-character rules for both front ends (telnet + SignalR).</summary>
public static class CharacterFactory
{
    /// <summary>The roles this account hasn't played yet (all five once it's played the lot).</summary>
    public static IReadOnlyList<CharacterClass> OfferedClasses(IReadOnlyList<CharacterSaveData> saved)
    {
        var played = saved
            .Select(c => Enum.TryParse<CharacterClass>(c.Class, ignoreCase: true, out var cc) ? cc : (CharacterClass?)null)
            .Where(c => c is not null).Select(c => c!.Value).ToHashSet();

        var offered = Enum.GetValues<CharacterClass>().Where(c => !played.Contains(c)).ToList();
        return offered.Count > 0 ? offered : Enum.GetValues<CharacterClass>().ToList();
    }

    /// <summary>A fresh Traveler with the console's starter kit: a wielded basic melee weapon (so the opening year-2000 fight isn't bare-fisted — playtested) plus three Field Rations.</summary>
    public static Traveler NewTraveler(string name, CharacterClass characterClass)
    {
        var traveler = new Traveler(name, characterClass);

        var starterWeapon = new Item("Standard-Issue Baton", ItemType.Weapon, 1, Rarity.Common, Value: 5, AttackBonus: 10);
        traveler.AddToInventory(starterWeapon);
        traveler.Wield(starterWeapon);

        for (var i = 0; i < 3; i++)
        {
            traveler.AddToInventory(Item.Create("Field Ration", ItemType.Consumable, 1, Rarity.Common,
                consumableEffect: ConsumableEffectType.Heal, effectMagnitude: 12));
        }

        return traveler;
    }
}
