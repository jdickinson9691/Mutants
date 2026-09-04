using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Diagnostics;
using ChronoTravelers.Core.Economy;
using ChronoTravelers.Core.Items;

namespace ChronoTravelers.Core.Tests.Diagnostics;

/// <summary>
/// Coverage for <see cref="PassiveActivationTracker"/> itself, plus a
/// couple of its real call sites — enough to lock in "no listener means no
/// cost/behavior change" and "a genuine activation reaches the listener
/// with a nonzero magnitude."
/// </summary>
public class PassiveActivationTrackerTests
{
    public PassiveActivationTrackerTests() => PassiveActivationTracker.Listener = null;

    private static Traveler LeveledTraveler(CharacterClass characterClass, int level)
    {
        var traveler = new Traveler("Test", characterClass);
        for (var i = 1; i < level; i++)
        {
            traveler.LevelUp();
        }

        return traveler;
    }

    [Fact]
    public void Record_WithNoListener_DoesNothing()
    {
        // Would throw a NullReferenceException if this didn't null-check.
        PassiveActivationTracker.Record(CharacterClass.Soldier, PassiveHook.ArmorDefenseBonusPct, 5);
    }

    [Fact]
    public void Record_WithZeroMagnitude_NeverInvokesTheListener()
    {
        var invoked = false;
        PassiveActivationTracker.Listener = (_, _, _) => invoked = true;

        PassiveActivationTracker.Record(CharacterClass.Soldier, PassiveHook.ArmorDefenseBonusPct, 0);

        Assert.False(invoked);
        PassiveActivationTracker.Listener = null;
    }

    [Fact]
    public void Record_WithNonzeroMagnitude_InvokesTheListenerWithTheGivenValues()
    {
        (CharacterClass Class, PassiveHook Hook, double Magnitude)? seen = null;
        PassiveActivationTracker.Listener = (cls, hook, magnitude) => seen = (cls, hook, magnitude);

        PassiveActivationTracker.Record(CharacterClass.Doctor, PassiveHook.HealRatioBonusPct, 3.5);

        Assert.Equal((CharacterClass.Doctor, PassiveHook.HealRatioBonusPct, 3.5), seen);
        PassiveActivationTracker.Listener = null;
    }

    [Fact]
    public void TakeDamage_SoldierSecondWind_ReportsAnActivationOnlyBelowThirtyPercentHp()
    {
        var soldier = LeveledTraveler(CharacterClass.Soldier, 8); // Second Wind unlocked
        var activations = new List<double>();
        PassiveActivationTracker.Listener = (cls, hook, magnitude) =>
        {
            if (cls == CharacterClass.Soldier && hook == PassiveHook.LowHpDamageReductionPct)
            {
                activations.Add(magnitude);
            }
        };

        soldier.TakeDamage(10); // full HP — Second Wind shouldn't fire
        Assert.Empty(activations);

        var maxHp = soldier.Health.Max;
        soldier.Health.Damage(maxHp - (int)(maxHp * 0.25)); // drop to 25% HP
        soldier.TakeDamage(10);

        var activation = Assert.Single(activations);
        Assert.True(activation > 0);
        PassiveActivationTracker.Listener = null;
    }

    [Fact]
    public void Store_SpyLightFingers_ReportsAStoreDiscountActivationOnBothSides()
    {
        var spy = LeveledTraveler(CharacterClass.Spy, 1); // Light Fingers unlocked at 1
        spy.AddCredits(1000);
        var store = Store.CreateGovernmentStore("Test Store", homeLevel: 1);
        var item = Item.Create("Widget", ItemType.Junk, 1, Rarity.Common);
        store.Stock(item, askingPrice: 100);

        var activations = new List<(PassiveHook Hook, double Magnitude)>();
        PassiveActivationTracker.Listener = (cls, hook, magnitude) =>
        {
            if (cls == CharacterClass.Spy && hook == PassiveHook.StoreDiscountBonusPct)
            {
                activations.Add((hook, magnitude));
            }
        };

        var listing = store.Listings[0];
        Assert.True(store.SellToTraveler(spy, listing));

        Assert.Single(activations);
        PassiveActivationTracker.Listener = null;
    }
}
