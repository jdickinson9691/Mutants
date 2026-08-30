using Mutants.Core.Classes;
using Mutants.Core.Ions;
using Mutants.Core.Items;
using Mutants.Core.Stats;

namespace Mutants.Core.Characters;

/// <summary>
/// A Mutant character — human player or NPC, both built on this exact same
/// type per docs/GDD.md §7 ("built on the exact same character/inventory/
/// ability code path" as the requirement that NPCs "play like players").
/// </summary>
public sealed class Mutant
{
    public string Name { get; }
    public CharacterClass Class { get; }
    public ClassDefinition ClassDefinition { get; }

    public int Level { get; private set; }
    public int Xp { get; private set; }
    public StatBlock Stats { get; private set; }
    public HealthPool Health { get; }
    public IonPool Ions { get; }

    /// <summary>The deepest time-travel level this Mutant has unlocked — drives the soft level cap.</summary>
    public int UnlockedTimeLevel { get; private set; }

    private readonly List<Item> _inventory = [];
    public IReadOnlyList<Item> Inventory => _inventory;

    public Item? EquippedWeapon { get; private set; }
    public Item? EquippedArmor { get; private set; }

    public Mutant(string name, CharacterClass characterClass, int unlockedTimeLevel = 1)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name cannot be empty.", nameof(name));
        }

        Name = name;
        Class = characterClass;
        ClassDefinition = ClassDefinition.For(characterClass);
        Level = 1;
        Xp = 0;
        Stats = ClassDefinition.BaseStats;
        UnlockedTimeLevel = unlockedTimeLevel;
        Health = new HealthPool(ClassDefinition.MaxHpAtLevel(Level));
        Ions = new IonPool(ClassDefinition.MaxIonsAtLevel(Level));
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
        var cap = Leveling.SoftLevelCap(UnlockedTimeLevel);

        while (Level < cap && Xp >= Leveling.CumulativeXpForLevel(Level + 1))
        {
            LevelUp();
            levelsGained++;
        }

        return levelsGained;
    }

    /// <summary>
    /// Raises Level by 1, grows the primary stat, and recalculates
    /// Max HP/Ions per docs/GDD.md §4.1 ("Every level grants a stat
    /// increase"). Does not enforce the soft cap itself — callers
    /// (GainXp, or explicit debug/testing use) decide whether to call it.
    /// </summary>
    public void LevelUp()
    {
        Level++;
        Stats = Stats.Increase(ClassDefinition.PrimaryStat);
        Health.SetMax(ClassDefinition.MaxHpAtLevel(Level));
        Ions.SetMax(ClassDefinition.MaxIonsAtLevel(Level));
    }

    /// <summary>Unlocks a deeper time-travel level, raising the soft level cap. Never regresses.</summary>
    public void UnlockTimeLevel(int timeLevel)
    {
        if (timeLevel > UnlockedTimeLevel)
        {
            UnlockedTimeLevel = timeLevel;
        }
    }

    public void AddToInventory(Item item) => _inventory.Add(item);

    /// <summary>
    /// Destroys an item from inventory for Ions — docs/GDD.md §2/§5.
    /// Returns the number of Ions actually gained (may be less than the
    /// item's convert value if it would overflow the Ion pool).
    /// </summary>
    public int Convert(Item item)
    {
        if (!_inventory.Remove(item))
        {
            throw new InvalidOperationException($"'{item.Name}' is not in {Name}'s inventory.");
        }

        return Ions.Add(item.ConvertValue());
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
            default:
                throw new InvalidOperationException($"Unexpected wieldable item type '{item.Type}'.");
        }
    }
}
