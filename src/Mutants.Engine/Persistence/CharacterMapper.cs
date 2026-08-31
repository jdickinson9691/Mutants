using Mutants.Core.Characters;
using Mutants.Core.Classes;
using Mutants.Core.Items;
using Mutants.Core.Stats;
using Mutants.Core.Time;
using Mutants.Core.World;

namespace Mutants.Engine.Persistence;

/// <summary>Converts between the live <see cref="Mutant"/> domain object and its <see cref="CharacterSaveData"/> save-file shape.</summary>
public static class CharacterMapper
{
    public static CharacterSaveData ToSaveData(Mutant mutant, long worldSeed)
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
            SchemaVersion = CharacterSaveData.CurrentSchemaVersion,
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
            WorldSeed = worldSeed,
            CurrentYear = mutant.CurrentYear,
            FurthestYearReached = mutant.FurthestYearReached,
            PositionEast = mutant.Position.East,
            PositionNorth = mutant.Position.North,
            DefeatedGatekeepers = mutant.DefeatedGatekeeperYears.OrderBy(y => y).ToList(),
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

        int currentYear;
        int furthestYear;
        IEnumerable<int> defeatedGatekeeperYears;

        if (data.SchemaVersion >= 2)
        {
            currentYear = data.CurrentYear;
            furthestYear = Math.Max(data.FurthestYearReached, data.CurrentYear);
            defeatedGatekeeperYears = data.DefeatedGatekeepers;
        }
        else
        {
            // Schema 1 → 2: map the old discrete level onto the timeline
            // (old level N ≈ year 2000 + (N-1)·375). The character survives;
            // the world reshuffles under a fresh seed (rolled by the caller),
            // so the old per-level gatekeeper flags no longer mean anything.
            currentYear = LegacyLevelToYear(data.CurrentTimeLevel);
            furthestYear = LegacyLevelToYear(Math.Max(data.UnlockedTimeLevel, data.CurrentTimeLevel));
            defeatedGatekeeperYears = [];
        }

        var mutant = Mutant.Restore(
            data.Name, characterClass, data.Level, data.Xp, stats,
            data.CurrentHp, data.MaxHp, data.CurrentIons, data.MaxIons, data.Riblets,
            currentYear, furthestYear,
            new Coordinate(data.PositionEast, data.PositionNorth),
            defeatedGatekeeperYears);

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

    /// <summary>Old discrete level N → the year that level occupied on the new timeline, clamped to 2000–5000.</summary>
    private static int LegacyLevelToYear(int level) =>
        Math.Clamp(TimeScale.MinYear + (Math.Max(1, level) - 1) * 375, TimeScale.MinYear, TimeScale.MaxYear);

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
