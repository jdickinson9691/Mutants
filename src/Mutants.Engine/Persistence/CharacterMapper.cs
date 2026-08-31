using Mutants.Core.Characters;
using Mutants.Core.Classes;
using Mutants.Core.Economy;
using Mutants.Core.Items;
using Mutants.Core.Stats;
using Mutants.Core.Time;
using Mutants.Core.World;

namespace Mutants.Engine.Persistence;

/// <summary>Converts between the live <see cref="Mutant"/> domain object and its <see cref="CharacterSaveData"/> save-file shape.</summary>
public static class CharacterMapper
{
    /// <summary>
    /// <paramref name="ownedStoresByYear"/> is the player's stores keyed
    /// by the year each is in — collect it from the live world before
    /// saving (see Mutants.Console). Omit it and no store state is written
    /// (used by tests that don't exercise stores).
    /// </summary>
    public static CharacterSaveData ToSaveData(
        Mutant mutant,
        long worldSeed,
        IReadOnlyDictionary<int, Store>? ownedStoresByYear = null)
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
            OwnedStores = (ownedStoresByYear ?? new Dictionary<int, Store>())
                .OrderBy(pair => pair.Key)
                .Select(pair => new OwnedStoreSaveData
                {
                    Year = pair.Key,
                    Capital = pair.Value.Capital,
                    Listings = pair.Value.Listings
                        .Select(l => new StoreListingSaveData { Item = ToItemSaveData(l.Item), AskingPrice = l.AskingPrice })
                        .ToList(),
                })
                .ToList(),
            SavedAtUtc = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Re-attaches the player's saved stores onto the freshly built
    /// world, after <see cref="FromSaveData"/> has restored the character.
    /// For each saved store, finds the (vacant) player slot in that year
    /// and restores it with the saved capital and listings — no Riblet
    /// charge. A saved year whose player slot isn't vacant (or doesn't
    /// exist) is skipped. Schema-1 blobs carry no <c>OwnedStores</c>, so
    /// this is a no-op for them.
    /// </summary>
    public static void ApplyOwnedStores(CharacterSaveData data, Mutant player, TimeWorld world)
    {
        foreach (var saved in data.OwnedStores)
        {
            if (!TimeScale.IsValidYear(saved.Year))
            {
                continue;
            }

            var slot = world.GetYear(saved.Year).StoreSlots.FirstOrDefault(s => s.IsAvailableForPurchase);
            if (slot is null)
            {
                continue;
            }

            var store = slot.RestoreOwnership(player, saved.Capital);
            foreach (var listing in saved.Listings)
            {
                store.Stock(FromItemSaveData(listing.Item), listing.AskingPrice);
            }
        }
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
        ConsumableEffect = item.ConsumableEffect.ToString(),
        EffectMagnitude = item.EffectMagnitude,
        EffectDurationTicks = item.EffectDurationTicks,
    };

    private static Item FromItemSaveData(ItemSaveData data) => new(
        data.Name,
        Enum.Parse<ItemType>(data.Type),
        data.Tier,
        Enum.Parse<Rarity>(data.Rarity),
        data.Value,
        data.AttackBonus,
        data.DefenseBonus,
        data.RestrictedClass is null ? null : Enum.Parse<CharacterClass>(data.RestrictedClass),
        Enum.TryParse<ConsumableEffectType>(data.ConsumableEffect, out var effect) ? effect : ConsumableEffectType.None,
        data.EffectMagnitude,
        data.EffectDurationTicks);
}
