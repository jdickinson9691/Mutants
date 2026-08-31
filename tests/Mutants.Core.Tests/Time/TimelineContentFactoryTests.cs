using Mutants.Core.Items;
using Mutants.Core.Monsters;
using Mutants.Core.Time;

namespace Mutants.Core.Tests.Time;

public class TimelineContentFactoryTests
{
    private static readonly SpeciesDefinition Baseline =
        new("grunt", "Grunt", [], MonsterArchetype.Baseline, ["common"]);

    private static readonly IReadOnlyList<ItemArchetypeDefinition> Pool =
    [
        new("scrap", "Scrap", ItemType.Junk, Rarity.Common, null, ConsumableEffectType.None, 0, 0, ["common"]),
        new("blade", "Blade", ItemType.Weapon, Rarity.Common, null, ConsumableEffectType.None, 0, 0, ["common"]),
    ];

    [Fact]
    public void ForSpecies_ScalesStatsUpWithTheYear()
    {
        var early = TimelineContentFactory.ForSpecies(1, Baseline, 2000, Pool)();
        var late = TimelineContentFactory.ForSpecies(1, Baseline, 5000, Pool)();

        Assert.True(late.Health.Max > early.Health.Max);
        Assert.True(late.AttackPower > early.AttackPower);
        Assert.True(late.XpReward > early.XpReward);
    }

    [Fact]
    public void ForSpecies_BaselineArchetypeMatchesMonsterScalingAtTheYearsTier()
    {
        var monster = TimelineContentFactory.ForSpecies(1, Baseline, 2375, Pool)();
        var tier = TimeScale.TierForYear(2375); // 2.0

        Assert.Equal((int)System.Math.Round(MonsterScaling.BaseHp(tier)), monster.Health.Max);
        Assert.Equal((int)System.Math.Round(MonsterScaling.BaseAttackPower(tier)), monster.AttackPower);
    }

    [Fact]
    public void ForSpecies_CasterIsGlassierThanBruiserAtTheSameYear()
    {
        var caster = TimelineContentFactory.ForSpecies(1,
            new SpeciesDefinition("c", "C", ["undead"], MonsterArchetype.Caster, ["common"]), 3000, Pool)();
        var bruiser = TimelineContentFactory.ForSpecies(1,
            new SpeciesDefinition("b", "B", [], MonsterArchetype.Bruiser, ["common"]), 3000, Pool)();

        Assert.True(caster.Health.Max < bruiser.Health.Max);
        Assert.True(caster.AttackPower > bruiser.AttackPower);
        Assert.True(caster.Speed > bruiser.Speed);
    }

    [Fact]
    public void ForSpecies_CarriesTagsThrough()
    {
        var monster = TimelineContentFactory.ForSpecies(1,
            new SpeciesDefinition("w", "Wraith", ["undead"], MonsterArchetype.Caster, ["common"]), 2500, Pool)();

        Assert.True(monster.HasTag("undead"));
    }

    [Fact]
    public void ForSpecies_LootTableDrawsOnlyFromTheGivenPoolAndIsDeterministic()
    {
        var a = TimelineContentFactory.ForSpecies(55, Baseline, 2600, Pool)();
        var b = TimelineContentFactory.ForSpecies(55, Baseline, 2600, Pool)();

        var poolNames = new HashSet<string> { "Scrap", "Blade" };
        Assert.All(a.LootTable, e => Assert.Contains(e.Item.Name, poolNames));
        Assert.Equal(a.LootTable.Select(e => e.Item.Name), b.LootTable.Select(e => e.Item.Name));
    }

    [Fact]
    public void Gatekeeper_IsABulletSpongeWithAGuaranteedLegendaryWeaponTrophy()
    {
        var year = 3210;
        var gatekeeper = TimelineContentFactory.Gatekeeper(worldSeed: 9, year);
        var regular = TimelineContentFactory.ForSpecies(9, Baseline, year, Pool)();

        Assert.True(gatekeeper.Health.Max > regular.Health.Max * 2);

        var drop = Assert.Single(gatekeeper.LootTable);
        Assert.Equal(1.0, drop.DropChance);
        Assert.Equal(ItemType.Weapon, drop.Item.Type);
        Assert.Equal(Rarity.Legendary, drop.Item.Rarity);
        Assert.Contains(year.ToString(), drop.Item.Name);
    }

    [Fact]
    public void Gatekeeper_IsDeterministicPerSeedAndYear()
    {
        var a = TimelineContentFactory.Gatekeeper(worldSeed: 3, 2600);
        var b = TimelineContentFactory.Gatekeeper(worldSeed: 3, 2600);
        Assert.Equal(a.LootTable[0].Item.Name, b.LootTable[0].Item.Name);
    }

    [Fact]
    public void ForArchetype_WeaponGetsAnAttackBonusThatGrowsWithTheYear()
    {
        var weapon = Pool[1];
        var early = TimelineContentFactory.ForArchetype(weapon, 2000);
        var late = TimelineContentFactory.ForArchetype(weapon, 5000);

        Assert.True(early.AttackBonus > 0);
        Assert.Equal(0, early.DefenseBonus);
        Assert.True(late.AttackBonus > early.AttackBonus);
        Assert.True(late.Value > early.Value);
    }

    [Fact]
    public void DisplayTier_IsAtLeastOneAndTracksTheYear()
    {
        Assert.Equal(1, TimelineContentFactory.DisplayTier(2000));
        Assert.True(TimelineContentFactory.DisplayTier(5000) > TimelineContentFactory.DisplayTier(2000));
    }
}
