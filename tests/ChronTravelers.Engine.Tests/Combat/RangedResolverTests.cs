using ChronTravelers.Core.Characters;
using ChronTravelers.Core.Classes;
using ChronTravelers.Core.Items;
using ChronTravelers.Core.Monsters;
using ChronTravelers.Engine.Combat;

namespace ChronTravelers.Engine.Tests.Combat;

public class RangedResolverTests
{
    private static StubRandomSource Neutral() => StubRandomSource.Fixed(0.5); // damage variance factor = 1.0

    private static Mutant Shooter() => new("Rook", CharacterClass.Warrior);

    private static Monster Target(int hp = 200, int defense = 8) =>
        new("Straw Dummy", tier: 2, maxHp: hp, attackPower: 5, defense: defense, speed: 8, xpReward: 80);

    [Fact]
    public void Fire_SpendsOneRoundOfAmmoAndDealsDamage()
    {
        var bow = Item.CreateRanged("Longbow", 2, Rarity.Uncommon, RangedKind.Bow, ammoCapacity: 10);
        var target = Target();

        var result = RangedResolver.Fire(Shooter(), target, bow, Neutral());

        Assert.Equal(9, bow.AmmoRemaining);
        Assert.True(result.Damage > 0);
        Assert.Equal(result.Damage, target.Health.Max - target.Health.Current);
        Assert.False(result.Killed);
    }

    [Fact]
    public void Fire_RaisesTheTargetsAggro_WhenItSurvives()
    {
        var bow = Item.CreateRanged("Longbow", 2, Rarity.Uncommon, RangedKind.Bow, ammoCapacity: 10);
        var target = Target();
        Assert.Equal(0, target.Aggro);

        RangedResolver.Fire(Shooter(), target, bow, Neutral());

        Assert.Equal(AggroModel.RangedHitAggro, target.Aggro);
    }

    [Fact]
    public void Fire_GunAndWandPierceArmour_BowDoesNot()
    {
        var bow = Item.CreateRanged("Bow", 2, Rarity.Uncommon, RangedKind.Bow, 10);
        var gun = Item.CreateRanged("Gun", 2, Rarity.Uncommon, RangedKind.Gun, 10);
        var wand = Item.CreateRanged("Wand", 2, Rarity.Uncommon, RangedKind.Wand, 10);

        var bowDmg = RangedResolver.Fire(Shooter(), Target(defense: 12), bow, Neutral()).Damage;
        var gunDmg = RangedResolver.Fire(Shooter(), Target(defense: 12), gun, Neutral()).Damage;
        var wandDmg = RangedResolver.Fire(Shooter(), Target(defense: 12), wand, Neutral()).Damage;

        Assert.True(gunDmg > bowDmg, "A gun ignores the target's armour.");
        Assert.True(wandDmg > bowDmg, "A wand ignores the target's armour.");
    }

    [Fact]
    public void Fire_MagnitudeScalesTheDamage()
    {
        var plain = Item.CreateRanged("Wand", 2, Rarity.Uncommon, RangedKind.Wand, 10, magnitude: 1.0);
        var strong = Item.CreateRanged("Wand", 2, Rarity.Uncommon, RangedKind.Wand, 10, magnitude: 2.0);

        var a = RangedResolver.Fire(Shooter(), Target(), plain, Neutral()).Damage;
        var b = RangedResolver.Fire(Shooter(), Target(), strong, Neutral()).Damage;

        Assert.Equal(a * 2, b);
    }

    [Fact]
    public void Fire_KillsWhenDamageExceedsHp()
    {
        var gun = Item.CreateRanged("Cannon", 5, Rarity.Legendary, RangedKind.Gun, 10, magnitude: 3.0);
        var target = Target(hp: 3);

        var result = RangedResolver.Fire(Shooter(), target, gun, Neutral());

        Assert.True(result.Killed);
        Assert.True(target.Health.IsDead);
    }

    [Fact]
    public void Fire_Weaken_SetsAPendingDefensePenaltyThatTheNextFightConsumesOnce()
    {
        var wand = Item.CreateRanged("Hexbolt", 2, Rarity.Rare, RangedKind.Wand, 10, RangedEffectType.Weaken, magnitude: 3.0);
        var target = Target(hp: 500);

        RangedResolver.Fire(Shooter(), target, wand, Neutral());
        Assert.Equal(3, target.PendingDefensePenalty);

        var session = new CombatSession(Shooter(), target, Neutral());
        Assert.Equal(0, target.PendingDefensePenalty); // spent
        Assert.Contains(session.Log, l => l.Contains("reeling"));

        // A brand-new fight against an un-weakened monster has no such line.
        var fresh = new CombatSession(Shooter(), Target(), Neutral());
        Assert.DoesNotContain(fresh.Log, l => l.Contains("reeling"));
    }

    [Fact]
    public void Fire_RejectsANonRangedOrDepletedWeapon()
    {
        var melee = Item.Create("Axe", ItemType.Weapon, 1, Rarity.Common);
        Assert.Throws<InvalidOperationException>(() => RangedResolver.Fire(Shooter(), Target(), melee, Neutral()));

        var spent = Item.CreateRanged("Sling", 1, Rarity.Common, RangedKind.Bow, ammoCapacity: 1);
        spent.AmmoRemaining = 0;
        Assert.Throws<InvalidOperationException>(() => RangedResolver.Fire(Shooter(), Target(), spent, Neutral()));
    }
}
