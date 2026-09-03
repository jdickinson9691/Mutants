using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Economy;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Stats;
using ChronoTravelers.Core.Time;
using ChronoTravelers.Core.World;

namespace ChronoTravelers.Engine.Persistence;

/// <summary>Converts between the live <see cref="Traveler"/> domain object and its <see cref="CharacterSaveData"/> save-file shape.</summary>
public static class CharacterMapper
{
    /// <summary>
    /// <paramref name="ownedStoresByYear"/> is the player's stores keyed
    /// by the year each is in — collect it from the live world before
    /// saving (see ChronoTravelers.Console). Omit it and no store state is written
    /// (used by tests that don't exercise stores).
    /// </summary>
    public static CharacterSaveData ToSaveData(
        Traveler traveler,
        long worldSeed,
        IReadOnlyDictionary<int, Store>? ownedStoresByYear = null)
    {
        var inventory = traveler.Inventory.Select(ToItemSaveData).ToList();

        // Item has no unique instance id (records compare by value), so if
        // two structurally identical wieldable items are both carried,
        // this can pick either one as "the" equipped index - functionally
        // harmless (they're identical), just not necessarily the exact
        // reference that was equipped.
        var inventoryList = traveler.Inventory.ToList();
        var equippedWeaponIndex = traveler.EquippedWeapon is null ? null : (int?)inventoryList.IndexOf(traveler.EquippedWeapon);
        var equippedArmorIndex = traveler.EquippedArmor is null ? null : (int?)inventoryList.IndexOf(traveler.EquippedArmor);
        // A ranged weapon carries a unique InstanceId, so IndexOf pins the exact instance (unlike weapon/armor above).
        var equippedRangedIndex = traveler.EquippedRanged is null ? null : (int?)inventoryList.IndexOf(traveler.EquippedRanged);

        return new CharacterSaveData
        {
            SchemaVersion = CharacterSaveData.CurrentSchemaVersion,
            Name = traveler.Name,
            Class = traveler.Class.ToString(),
            Level = traveler.Level,
            Xp = traveler.Xp,
            Strength = traveler.Stats.Strength,
            Agility = traveler.Stats.Agility,
            Resolve = traveler.Stats.Resolve,
            Intellect = traveler.Stats.Intellect,
            CurrentHp = traveler.Health.Current,
            MaxHp = traveler.Health.Max,
            CurrentTachyons = traveler.Tachyons.Current,
            MaxTachyons = traveler.Tachyons.Max,
            Credits = traveler.Credits,
            WorldSeed = worldSeed,
            CurrentYear = traveler.CurrentYear,
            FurthestYearReached = traveler.FurthestYearReached,
            PositionEast = traveler.Position.East,
            PositionNorth = traveler.Position.North,
            DefeatedWardens = traveler.DefeatedWardenYears.OrderBy(y => y).ToList(),
            ElixirUsesByStat = traveler.ElixirUsesByStat.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            Inventory = inventory,
            EquippedWeaponIndex = equippedWeaponIndex >= 0 ? equippedWeaponIndex : null,
            EquippedArmorIndex = equippedArmorIndex >= 0 ? equippedArmorIndex : null,
            EquippedRangedIndex = equippedRangedIndex >= 0 ? equippedRangedIndex : null,
            OwnedStores = (ownedStoresByYear ?? new Dictionary<int, Store>())
                .OrderBy(pair => pair.Key)
                .Select(pair => new OwnedStoreSaveData
                {
                    Year = pair.Key,
                    Capital = pair.Value.Capital,
                    TachyonReserve = pair.Value.TachyonReserve,
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
    /// and restores it with the saved capital and listings — no Credit
    /// charge. A saved year whose player slot isn't vacant (or doesn't
    /// exist) is skipped. Schema-1 blobs carry no <c>OwnedStores</c>, so
    /// this is a no-op for them.
    /// </summary>
    public static void ApplyOwnedStores(CharacterSaveData data, Traveler player, TimeWorld world)
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

            var store = slot.RestoreOwnership(player, saved.Capital, saved.TachyonReserve);
            foreach (var listing in saved.Listings)
            {
                // enforceCap: false — same reasoning as FromSaveData above:
                // a save from before Store.MaxListings existed must not
                // lose stock on load.
                store.Stock(FromItemSaveData(listing.Item), listing.AskingPrice, enforceCap: false);
            }
        }
    }

    public static Traveler FromSaveData(CharacterSaveData data)
    {
        var characterClass = Enum.Parse<CharacterClass>(data.Class);
        var stats = new StatBlock(data.Strength, data.Agility, data.Resolve, data.Intellect);

        int currentYear;
        int furthestYear;
        IEnumerable<int> defeatedWardenYears;

        var elixirUsesByStat = data.ElixirUsesByStat
            .Where(kv => Enum.TryParse<PrimaryStat>(kv.Key, ignoreCase: true, out _))
            .Select(kv => new KeyValuePair<PrimaryStat, int>(Enum.Parse<PrimaryStat>(kv.Key, ignoreCase: true), kv.Value));

        if (data.SchemaVersion >= 2)
        {
            currentYear = data.CurrentYear;
            furthestYear = Math.Max(data.FurthestYearReached, data.CurrentYear);
            defeatedWardenYears = data.DefeatedWardens;
        }
        else
        {
            // Schema 1 → 2: map the old discrete level onto the timeline
            // (old level N ≈ year 2000 + (N-1)·375). The character survives;
            // the world reshuffles under a fresh seed (rolled by the caller),
            // so the old per-level warden flags no longer mean anything.
            currentYear = LegacyLevelToYear(data.CurrentTimeLevel);
            furthestYear = LegacyLevelToYear(Math.Max(data.UnlockedTimeLevel, data.CurrentTimeLevel));
            defeatedWardenYears = [];
        }

        var traveler = Traveler.Restore(
            data.Name, characterClass, data.Level, data.Xp, stats,
            data.CurrentHp, data.MaxHp, data.CurrentTachyons, data.MaxTachyons, data.Credits,
            currentYear, furthestYear,
            new Coordinate(data.PositionEast, data.PositionNorth),
            defeatedWardenYears, elixirUsesByStat);

        var items = data.Inventory.Select(FromItemSaveData).ToList();
        foreach (var item in items)
        {
            // enforceCap: false — a save written before Traveler.MaxInventorySize
            // existed may carry more than 15 items; loading must never
            // silently drop a returning player's belongings. The cap only
            // stops new pickups going forward (Traveler.AddToInventory's
            // normal default), not what a save already holds.
            traveler.AddToInventory(item, enforceCap: false);
        }

        if (data.EquippedWeaponIndex is { } weaponIndex && weaponIndex >= 0 && weaponIndex < items.Count)
        {
            traveler.Wield(items[weaponIndex]);
        }

        if (data.EquippedArmorIndex is { } armorIndex && armorIndex >= 0 && armorIndex < items.Count)
        {
            traveler.Wield(items[armorIndex]);
        }

        if (data.EquippedRangedIndex is { } rangedIndex && rangedIndex >= 0 && rangedIndex < items.Count)
        {
            traveler.Wield(items[rangedIndex]);
        }

        return traveler;
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
        RangedKind = item.RangedKind.ToString(),
        AmmoCapacity = item.AmmoCapacity,
        AmmoRemaining = item.AmmoRemaining,
        RangedEffect = item.RangedEffect.ToString(),
        InstanceId = item.InstanceId == Guid.Empty ? "" : item.InstanceId.ToString(),
        IsTimeShard = item.IsTimeShard,
        Range = item.Range,
    };

    private static Item FromItemSaveData(ItemSaveData data)
    {
        var rangedKind = Enum.TryParse<RangedKind>(data.RangedKind, out var rk) ? rk : RangedKind.None;
        var isRanged = rangedKind != RangedKind.None;

        var item = new Item(
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
            data.EffectDurationTicks,
            rangedKind,
            data.AmmoCapacity,
            Enum.TryParse<RangedEffectType>(data.RangedEffect, out var rangedEffect) ? rangedEffect : RangedEffectType.None,
            isRanged
                ? (Guid.TryParse(data.InstanceId, out var id) && id != Guid.Empty ? id : Guid.NewGuid())
                : Guid.Empty,
            data.IsTimeShard,
            Range: isRanged ? Math.Clamp(data.Range == 0 ? 1 : data.Range, 1, 4) : 1);

        if (isRanged)
        {
            item.AmmoRemaining = data.AmmoCapacity > 0
                ? Math.Clamp(data.AmmoRemaining, 0, data.AmmoCapacity)
                : Math.Max(0, data.AmmoRemaining);
        }

        return item;
    }
}
