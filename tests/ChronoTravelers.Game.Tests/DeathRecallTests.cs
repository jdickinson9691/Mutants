using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Time;
using ChronoTravelers.Engine;

namespace ChronoTravelers.Game.Tests;

/// <summary>
/// Unit coverage for <see cref="DeathRecall"/> — docs/GDD.md §3.3's death &
/// recall, shared by both front ends (the console's <c>HandleFight</c>
/// defeat branch and its world-tick ambush-death branch, and the server's
/// <c>SharedGame.Respawn</c>). Exercises the mechanic directly rather than
/// through a full fight, so these assertions aren't at the mercy of combat
/// RNG.
/// </summary>
public class DeathRecallTests
{
    private static Item Junk(string name) => new(name, ItemType.Junk, 1, Rarity.Common, Value: 5);
    private static Item Weapon(string name) => new(name, ItemType.Weapon, 1, Rarity.Common, Value: 5, AttackBonus: 10);

    [Fact]
    public void Apply_DropsHalfOfUnequippedInventory_AsGroundLootAtTheDeathSpot()
    {
        var world = TestTimeWorld.Build();
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        traveler.SetCurrentYear(3000);
        var deathRoom = world.GetYear(3000).Map.Start;
        traveler.PlaceAt(deathRoom);

        var weapon = Weapon("Standard-Issue Baton");
        traveler.AddToInventory(weapon);
        traveler.Wield(weapon);

        var junkItems = new[] { Junk("Scrap A"), Junk("Scrap B"), Junk("Scrap C"), Junk("Scrap D") };
        foreach (var item in junkItems)
        {
            traveler.AddToInventory(item);
        }

        var outcome = DeathRecall.Apply(traveler, world, new SystemRandomSource());

        // floor(4 * 0.5) = 2 of the 4 unequipped junk items dropped; the
        // equipped weapon is never a candidate.
        Assert.Equal(2, outcome.DroppedItems.Count);
        Assert.DoesNotContain(weapon, outcome.DroppedItems);
        Assert.All(outcome.DroppedItems, item => Assert.Contains(item, junkItems));

        // Dropped items actually left the inventory...
        foreach (var dropped in outcome.DroppedItems)
        {
            Assert.DoesNotContain(dropped, traveler.Inventory);
        }

        // ...and landed as ground loot in the year/room they died in, not
        // wherever they ended up after the recall.
        var groundLoot = world.GetYear(3000).Population.LootAt(deathRoom);
        foreach (var dropped in outcome.DroppedItems)
        {
            Assert.Contains(dropped, groundLoot);
        }

        // Equipped weapon survives the recall, still wielded.
        Assert.Contains(weapon, traveler.Inventory);
        Assert.Equal(weapon, traveler.EquippedWeapon);

        Assert.Equal(3000, outcome.DeathYear);
        Assert.Equal(deathRoom, outcome.DeathPosition);
    }

    [Fact]
    public void Apply_NeverDropsEquippedGear_EvenWhenItsTheOnlyInventory()
    {
        var world = TestTimeWorld.Build();
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        var weapon = Weapon("Standard-Issue Baton");
        traveler.AddToInventory(weapon);
        traveler.Wield(weapon);

        var outcome = DeathRecall.Apply(traveler, world, new SystemRandomSource());

        Assert.Empty(outcome.DroppedItems);
        Assert.Contains(weapon, traveler.Inventory);
    }

    [Fact]
    public void Apply_SpendsHalfOfCurrentTachyons()
    {
        var world = TestTimeWorld.Build();
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        var before = traveler.Tachyons.Current;

        var outcome = DeathRecall.Apply(traveler, world, new SystemRandomSource());

        var expectedLost = (int)Math.Round(before * DeathRecall.TachyonPenaltyFraction);
        Assert.Equal(expectedLost, outcome.TachyonsLost);
        Assert.Equal(before - expectedLost, traveler.Tachyons.Current);
    }

    [Fact]
    public void Apply_SnapsBackToYearTwoThousandAtFullHealth()
    {
        var world = TestTimeWorld.Build();
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        traveler.SetCurrentYear(4200);
        traveler.PlaceAt(world.GetYear(4200).Map.Start);
        traveler.Health.Damage(traveler.Health.Max); // simulate the killing blow

        DeathRecall.Apply(traveler, world, new SystemRandomSource());

        Assert.False(traveler.Health.IsDead);
        Assert.Equal(traveler.Health.Max, traveler.Health.Current);
        Assert.Equal(TimeScale.MinYear, traveler.CurrentYear);
        Assert.Equal(world.GetYear(TimeScale.MinYear).Map.Start, traveler.Position);
    }

    [Fact]
    public void Apply_LeavesLevelStatsAndCreditsUntouched()
    {
        var world = TestTimeWorld.Build();
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        traveler.AddCredits(250);
        var levelBefore = traveler.Level;
        var creditsBefore = traveler.Credits;

        DeathRecall.Apply(traveler, world, new SystemRandomSource());

        Assert.Equal(levelBefore, traveler.Level);
        Assert.Equal(creditsBefore, traveler.Credits);
    }
}
