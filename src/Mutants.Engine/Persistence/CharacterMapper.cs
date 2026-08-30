using Mutants.Core.Characters;
using Mutants.Core.Classes;
using Mutants.Core.Items;
using Mutants.Core.Stats;
using Mutants.Core.World;

namespace Mutants.Engine.Persistence;

/// <summary>Converts between the live <see cref="Mutant"/> domain object and its <see cref="CharacterSaveData"/> save-file shape.</summary>
public static class CharacterMapper
{
    public static CharacterSaveData ToSaveData(Mutant mutant)
    {
        var inventory = mutant.Inventory.Select(ToItemSaveData).ToList();

        // Item has no unique instance id (records compare by value), so if
        // two structurally identical wieldable items are both carried,
        // this can pick either one as "the" equipped index - functionally
        // harmless (they're identical), just not necessarily the exact
        // reference that was equipped.
        var inventoryList = mutant.Inventory.ToList();
        var equippedWeaponIndex = mutant.EquippedWeapon is null ? null : (int?)inventoryList.IndexOf(mutant.EquippedWeapon);
        var equippedArmorIndex = mutant.EquippedArmor is null ? null : (int?)inventoryList.IndexOf(mutant.EquippedArmor);

        return new CharacterSaveData
        {
            Name = mutant.Name,
            Class = mutant.Class.ToString(),
            Level = mutant.Level,
            Xp = mutant.Xp,
            Strength = mutant.Stats.Strength,
            Agility = mutant.Stats.Agility,
            Faith = mutant.Stats.Faith,
            Intellect = mutant.Stats.Intellect,
            CurrentHp = mutant.Health.Current,
            MaxHp = mutant.Health.Max,
            CurrentIons = mutant.Ions.Current,
            MaxIons = mutant.Ions.Max,
            Riblets = mutant.Riblets,
            UnlockedTimeLevel = mutant.UnlockedTimeLevel,
            CurrentTimeLevel = mutant.CurrentTimeLevel,
            PositionEast = mutant.Position.East,
            PositionNorth = mutant.Position.North,
            DefeatedGatekeepers = Enumerable.Range(1, mutant.UnlockedTimeLevel)
                .Where(mutant.HasDefeatedGatekeeper)
                .ToList(),
            Inventory = inventory,
            EquippedWeaponIndex = equippedWeaponIndex >= 0 ? equippedWeaponIndex : null,
            EquippedArmorIndex = equippedArmorIndex >= 0 ? equippedArmorIndex : null,
            SavedAtUtc = DateTime.UtcNow,
        };
    }

    public static Mutant FromSaveData(CharacterSaveData data)
    {
        var characterClass = Enum.Parse<CharacterClass>(data.Class);
        var stats = new StatBlock(data.Strength, data.Agility, data.Faith, data.Intellect);

        var mutant = Mutant.Restore(
            data.Name, characterClass, data.Level, data.Xp, stats,
            data.CurrentHp, data.MaxHp, data.CurrentIons, data.MaxIons, data.Riblets,
            data.UnlockedTimeLevel, data.CurrentTimeLevel,
            new Coordinate(data.PositionEast, data.PositionNorth),
            data.DefeatedGatekeepers);

        var items = data.Inventory.Select(FromItemSaveData).ToList();
        foreach (var item in items)
        {
            mutant.AddToInventory(item);
        }

        if (data.EquippedWeaponIndex is { } weaponIndex && weaponIndex >= 0 && weaponIndex < items.Count)
        {
            mutant.Wield(items[weaponIndex]);
        }

        if (data.EquippedArmorIndex is { } armorIndex && armorIndex >= 0 && armorIndex < items.Count)
        {
            mutant.Wield(items[armorIndex]);
        }

        return mutant;
    }

    private static ItemSaveData ToItemSaveData(Item item) => new()
    {
        Name = item.Name,
        Type = item.Type.ToString(),
        Tier = item.Tier,
        Rarity = item.Rarity.ToString(),
        Value = item.Value,
        AttackBonus = item.AttackBonus,
        DefenseBonus = item.DefenseBonus,
        RestrictedClass = item.RestrictedClass?.ToString(),
    };

    private static Item FromItemSaveData(ItemSaveData data) => new(
        data.Name,
        Enum.Parse<ItemType>(data.Type),
        data.Tier,
        Enum.Parse<Rarity>(data.Rarity),
        data.Value,
        data.AttackBonus,
        data.DefenseBonus,
        data.RestrictedClass is null ? null : Enum.Parse<CharacterClass>(data.RestrictedClass));
}
