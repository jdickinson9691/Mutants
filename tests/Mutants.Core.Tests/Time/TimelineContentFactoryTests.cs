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
        var tier = TimeScale.TierForYear(2375); // 2.5 on the steep early slope

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
    public void ForSpecies_LootTable_HasSellGearAndUseEntries_WithSellFodderTheLikeliest()
    {
        IReadOnlyList<ItemArchetypeDefinition> pool =
        [
            new("j", "Bits", ItemType.Junk, Rarity.Common, null, ConsumableEffectType.None, 0, 0, ["common"]),
            new("w", "Blade", ItemType.Weapon, Rarity.Uncommon, null, ConsumableEffectType.None, 0, 0, ["common"], PowerMultiplier: 1.0),
            new("p", "Tonic", ItemType.Consumable, Rarity.Common, null, ConsumableEffectType.Heal, 10, 0, ["common"]),
        ];

        var table = TimelineContentFactory.ForSpecies(7, Baseline, 2600, pool)().LootTable;

        var junkChances = table.Where(e => e.Item.Type == ItemType.Junk).Select(e => e.DropChance).ToList();
        var gear = table.Single(e => e.Item.Type == ItemType.Weapon);
        var use = table.Single(e => e.Item.Type == ItemType.Consumable);

        Assert.True(junkChances.Count >= 2, "there's a near-certain junk drop plus a chance at a second");
        Assert.True(junkChances.Max() > gear.DropChance, "sell fodder is the most common drop");
        Assert.True(gear.DropChance > use.DropChance);
        Assert.True(junkChances.Max() >= 0.8, "a kill reliably yields something to sell/convert");
        Assert.True(junkChances.Sum() > 1.0, "expected junk per kill is above one");
    }

    [Fact]
    public void ForSpecies_LootTable_StillYieldsAPayoutWhenThePoolIsAllOneCategory()
    {
        IReadOnlyList<ItemArchetypeDefinition> weaponsOnly =
        [
            new("w", "Blade", ItemType.Weapon, Rarity.Common, null, ConsumableEffectType.None, 0, 0, ["common"], PowerMultiplier: 0.6),
        ];

        var table = TimelineContentFactory.ForSpecies(7, Baseline, 2600, weaponsOnly)().LootTable;

        Assert.NotEmpty(table);
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

    [Fact]
    public void ForArchetype_WeaponPowerDrivesBothDamageAndRarity()
    {
        ItemArchetypeDefinition Weapon(string id, double power) =>
            new(id, id, ItemType.Weapon, RarityExtensions.ForPower(power), null,
                ConsumableEffectType.None, 0, 0, ["common"], PowerMultiplier: power);

        var crude = TimelineContentFactory.ForArchetype(Weapon("crude", 0.5), 2600);
        var relic = TimelineContentFactory.ForArchetype(Weapon("relic", 2.9), 2600);

        Assert.True(relic.AttackBonus > crude.AttackBonus * 3, "a relic weapon hits far harder");
        Assert.Equal(Rarity.Common, crude.Rarity);
        Assert.Equal(Rarity.Legendary, relic.Rarity);
    }

    [Fact]
    public void BuildLootTable_WeightsPicksSoRareWeaponsAreScarce()
    {
        // A pool with a weapon in every rarity band; the crude one and the
        // relic one are equally "present" — only the drop weighting differs.
        ItemArchetypeDefinition W(string id, double power) =>
            new(id, id, ItemType.Weapon, RarityExtensions.ForPower(power), null,
                ConsumableEffectType.None, 0, 0, ["common"], PowerMultiplier: power);
        IReadOnlyList<ItemArchetypeDefinition> pool =
            [W("crude", 0.5), W("std", 1.0), W("fine", 1.6), W("master", 2.2), W("relic", 2.9)];

        var byRarity = new Dictionary<Rarity, int>();
        for (var year = 2000; year < 3000; year++)
        {
            var monster = TimelineContentFactory.ForSpecies(worldSeed: 42, Baseline, year, pool)();
            foreach (var entry in monster.LootTable)
            {
                byRarity[entry.Item.Rarity] = byRarity.GetValueOrDefault(entry.Item.Rarity) + 1;
            }
        }

        var total = byRarity.Values.Sum();
        Assert.True(byRarity.GetValueOrDefault(Rarity.Common) + byRarity.GetValueOrDefault(Rarity.Uncommon) > total * 0.6,
            "the bulk of weapon drops should be the low bands");
        Assert.True(byRarity.GetValueOrDefault(Rarity.Legendary) < total * 0.08,
            "a relic weapon should be a rare pull");
    }

    [Fact]
    public void Gatekeeper_TrophyIsALegendaryWeaponThatOutclassesAStandardOne()
    {
        var trophy = TimelineContentFactory.Gatekeeper(worldSeed: 7, 3000).LootTable[0].Item;
        var standard = TimelineContentFactory.ForArchetype(
            new("std", "Std", ItemType.Weapon, Rarity.Uncommon, null, ConsumableEffectType.None, 0, 0, ["common"]),
            3000);

        Assert.Equal(Rarity.Legendary, trophy.Rarity);
        Assert.True(trophy.AttackBonus > standard.AttackBonus * 2);
    }
}
