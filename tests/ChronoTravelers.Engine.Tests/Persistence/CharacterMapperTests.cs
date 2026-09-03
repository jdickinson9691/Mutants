using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Stats;
using ChronoTravelers.Engine.Persistence;

namespace ChronoTravelers.Engine.Tests.Persistence;

public class CharacterMapperTests
{
    private static Traveler Soldier() => new("Rook", CharacterClass.Soldier);

    [Fact]
    public void RoundTrip_PreservesARangedWeaponsRange()
    {
        var traveler = Soldier();
        var bow = Item.CreateRanged("Longbow", 3, Rarity.Uncommon, RangedKind.Bow, ammoCapacity: 5, range: 3);
        traveler.AddToInventory(bow);
        traveler.Wield(bow);

        var saved = CharacterMapper.ToSaveData(traveler, worldSeed: 1);
        var restored = CharacterMapper.FromSaveData(saved);

        Assert.Equal(3, restored.EquippedRanged!.Range);
    }

    [Fact]
    public void RoundTrip_OldBlobWithNoRangeField_DefaultsToOne()
    {
        // Simulates a pre-Range-field save: ItemSaveData.Range defaults to
        // 0 (uninitialized int), which FromSaveData must treat as "1", not
        // an invalid range.
        var data = new CharacterSaveData
        {
            Name = "Legacy",
            Class = "Soldier",
            Level = 1,
            Strength = 15,
            Agility = 10,
            Resolve = 8,
            Intellect = 8,
            MaxHp = 30,
            CurrentHp = 30,
            MaxTachyons = 60,
            CurrentTachyons = 60,
            CurrentYear = 2000,
            FurthestYearReached = 2000,
            Inventory = [new ItemSaveData
            {
                Name = "Old Sling", Type = "Ranged", Tier = 1, Rarity = "Common",
                RangedKind = "Bow", AmmoCapacity = 5, AmmoRemaining = 5,
                InstanceId = Guid.NewGuid().ToString(),
                // Range intentionally left at its default (0).
            }],
            EquippedRangedIndex = 0,
        };

        var restored = CharacterMapper.FromSaveData(data);

        Assert.Equal(1, restored.EquippedRanged!.Range);
    }

    [Fact]
    public void RoundTrip_PreservesElixirDiminishingReturnsState()
    {
        var traveler = Soldier();
        var elixir1 = Item.Create("Meridian Serum: Strength", ItemType.Consumable, 3, Rarity.Epic,
            consumableEffect: ConsumableEffectType.BoostStrength, effectMagnitude: 5);
        traveler.AddToInventory(elixir1);
        traveler.Consume(elixir1); // first use: full +5, records one use on Strength

        var saved = CharacterMapper.ToSaveData(traveler, worldSeed: 1);
        var restored = CharacterMapper.FromSaveData(saved);

        var elixir2 = Item.Create("Meridian Serum: Strength", ItemType.Consumable, 3, Rarity.Epic,
            consumableEffect: ConsumableEffectType.BoostStrength, effectMagnitude: 5);
        restored.AddToInventory(elixir2);
        var strengthBeforeSecondDrink = restored.Stats.Strength;

        restored.Consume(elixir2);

        // Second use on the same stat must be diminished (0.75 falloff),
        // not a fresh full +5 — otherwise saving/reloading between drinks
        // would let a player dodge the falloff entirely.
        var gained = restored.Stats.Strength - strengthBeforeSecondDrink;
        Assert.True(gained < 5, $"expected a diminished second boost, got +{gained}");
        Assert.True(gained >= 1);
    }

    [Fact]
    public void RoundTrip_OldBlobWithNoElixirUsageField_StartsUndiminished()
    {
        var data = new CharacterSaveData
        {
            Name = "Legacy",
            Class = "Soldier",
            Level = 1,
            Strength = 15,
            Agility = 10,
            Resolve = 8,
            Intellect = 8,
            MaxHp = 30,
            CurrentHp = 30,
            MaxTachyons = 60,
            CurrentTachyons = 60,
            CurrentYear = 2000,
            FurthestYearReached = 2000,
            // ElixirUsesByStat left at its default empty map.
        };

        var restored = CharacterMapper.FromSaveData(data);
        var elixir = Item.Create("Meridian Serum: Strength", ItemType.Consumable, 3, Rarity.Epic,
            consumableEffect: ConsumableEffectType.BoostStrength, effectMagnitude: 5);
        restored.AddToInventory(elixir);

        restored.Consume(elixir);

        Assert.Equal(20, restored.Stats.Strength); // full, undiminished +5
    }
}
