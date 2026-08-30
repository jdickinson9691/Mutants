using Mutants.Core.Classes;
using Mutants.Core.Ions;
using Mutants.Core.Items;
using Mutants.Core.Stats;
using Mutants.Core.World;

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

    /// <summary>Current grid position on whichever <see cref="LevelMap"/> this Mutant is on — docs/GDD.md §3.1.</summary>
    public Coordinate Position { get; private set; } = Coordinate.Origin;

    /// <summary>
    /// Riblets on hand — docs/GDD.md §6's store currency. Full store
    /// buy/sell logic is future work (milestone 5); this is just the
    /// balance, needed now for the console status bar (§10).
    /// </summary>
    public int Riblets { get; private set; }

    private readonly List<Item> _inventory = [];
    public IReadOnlyList<Item> Inventory => _inventory;

    public Item? EquippedWeapon { get; private set; }
    public Item? EquippedArmor { get; private set; }

    /// <summary>
    /// Turn order / "who acts first" stat for combat — original design
    /// (not GDD-specified), currently just the raw Agility stat.
    /// </summary>
    public int Speed => Stats.Agility;

    /// <summary>
    /// Combat attack power: primary stat + equipped weapon's AttackBonus,
    /// scaled by <see cref="Item.WieldEffectiveness"/> (so off-class
    /// weapons contribute less, per docs/GDD.md §4.3). Original design —
    /// the GDD confirms "a primary attack" per class but not its formula.
    /// </summary>
    public int EffectiveAttackPower
    {
        get
        {
            var basePower = Stats.Get(ClassDefinition.PrimaryStat);
            var weaponBonus = EquippedWeapon is null
                ? 0
                : (int)Math.Round(EquippedWeapon.AttackBonus * EquippedWeapon.WieldEffectiveness(Class));

            return basePower + weaponBonus;
        }
    }

    /// <summary>Combat defense: half of Agility + equipped armor's DefenseBonus (scaled by wield effectiveness). Original design.</summary>
    public int EffectiveDefense
    {
        get
        {
            var baseDefense = Stats.Agility / 2;
            var armorBonus = EquippedArmor is null
                ? 0
                : (int)Math.Round(EquippedArmor.DefenseBonus * EquippedArmor.WieldEffectiveness(Class));

            return baseDefense + armorBonus;
        }
    }

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

    /// <summary>Places the Mutant at a specific grid position — e.g. spawning them at a level's start room.</summary>
    public void PlaceAt(Coordinate coordinate) => Position = coordinate;

    /// <summary>Moves the Mutant to an adjacent coordinate. Legality (is there really an exit there?) is the caller's job — see <see cref="LevelMap.TryMove"/>.</summary>
    public void MoveTo(Coordinate coordinate) => Position = coordinate;

    public void AddRiblets(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount cannot be negative.");
        }

        Riblets += amount;
    }

    public void SpendRiblets(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount cannot be negative.");
        }

        if (amount > Riblets)
        {
            throw new InvalidOperationException($"Cannot spend {amount} Riblets with only {Riblets} available.");
        }

        Riblets -= amount;
    }

    public void AddToInventory(Item item) => _inventory.Add(item);

    /// <summary>
    /// Destroys an item from inventory for Ions — docs/GDD.md §2/§5.
    /// Returns the number of Ions actually gained (may be less than the
    /// item's convert value if it would overflow the Ion pool). Unequips
    /// the item first if it was wielded.
    /// </summary>
    public int Convert(Item item)
    {
        RemoveFromInventoryOrThrow(item);
        return Ions.Add(item.ConvertValue());
    }

    /// <summary>
    /// Sells an item from inventory for Riblets — docs/GDD.md §5/§6. See
    /// <see cref="Item.SellValue"/> for why this is a flat placeholder
    /// price pending the real store system (milestone 5). Unequips the
    /// item first if it was wielded.
    /// </summary>
    public int Sell(Item item)
    {
        RemoveFromInventoryOrThrow(item);
        var riblets = item.SellValue();
        AddRiblets(riblets);
        return riblets;
    }

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
