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

    /// <summary>
    /// A fresh Traveler with the console's starter kit: a wielded basic
    /// melee weapon (so the opening year-2000 fight isn't bare-fisted —
    /// playtested) plus three Field Rations.
    ///
    /// Also a wielded basic armor piece — every class previously started
    /// with <c>EquippedArmor == null</c>, so <see cref="Traveler.EffectiveDefense"/>
    /// was Agility-only (as low as 3 for Doctor/Scientist). Combined with
    /// <c>MonsterController.ResolveAmbush</c> halving that already-thin
    /// number before combat even starts, and no ambush-mitigation passive
    /// existing before level 7/23, this made a level-1 double-hit (ambush,
    /// then a same-or-faster monster's first strike) a near-guaranteed
    /// death for Doctor/Scientist/Spy/Engineer on every seed — not RNG,
    /// structural. Granting a real starter armor item lifts the whole
    /// floor the same way the starter weapon already does for Attack.
    /// </summary>
    public static Traveler NewTraveler(string name, CharacterClass characterClass)
    {
        var traveler = new Traveler(name, characterClass);

        var starterWeapon = new Item("Standard-Issue Baton", ItemType.Weapon, 1, Rarity.Common, Value: 5, AttackBonus: 10);
        traveler.AddToInventory(starterWeapon);
        traveler.Wield(starterWeapon);

        var starterArmor = new Item("Standard-Issue Vest", ItemType.Armor, 1, Rarity.Common, Value: 5, DefenseBonus: 8);
        traveler.AddToInventory(starterArmor);
        traveler.Wield(starterArmor);

        for (var i = 0; i < 3; i++)
        {
            traveler.AddToInventory(Item.Create("Field Ration", ItemType.Consumable, 1, Rarity.Common,
                consumableEffect: ConsumableEffectType.Heal, effectMagnitude: 12));
        }

        return traveler;
    }
}
