using ChronoTravelers.Core.Classes;

namespace ChronoTravelers.Core.Tests.Classes;

public class ClassDefinitionTests
{
    [Fact]
    public void All_ContainsExactlyTheFiveSourcedClasses()
    {
        var expected = new[]
        {
            CharacterClass.Soldier, CharacterClass.Spy, CharacterClass.Doctor,
            CharacterClass.Scientist, CharacterClass.Engineer,
        };

        Assert.Equal(expected.Length, ClassDefinition.All.Count);
        foreach (var characterClass in expected)
        {
            Assert.True(ClassDefinition.All.ContainsKey(characterClass));
        }
    }

    [Theory]
    [InlineData(CharacterClass.Soldier, PrimaryStat.Strength)]
    [InlineData(CharacterClass.Spy, PrimaryStat.Agility)]
    [InlineData(CharacterClass.Doctor, PrimaryStat.Resolve)]
    [InlineData(CharacterClass.Scientist, PrimaryStat.Intellect)]
    [InlineData(CharacterClass.Engineer, PrimaryStat.Intellect)]
    public void PrimaryStat_MatchesGddTable(CharacterClass characterClass, PrimaryStat expected)
    {
        Assert.Equal(expected, ClassDefinition.For(characterClass).PrimaryStat);
    }

    [Fact]
    public void Warrior_HasHighestBaseAndPerLevelHp()
    {
        var warriorHp = ClassDefinition.For(CharacterClass.Soldier).BaseHp;
        foreach (var other in ClassDefinition.All.Values.Where(d => d.Class != CharacterClass.Soldier))
        {
            Assert.True(warriorHp >= other.BaseHp,
                $"Soldier base HP ({warriorHp}) should be >= {other.Class} ({other.BaseHp}) per GDD §4 'best HP'.");
        }
    }

    [Fact]
    public void ArcaneClasses_DrainTachyonsFasterThanMeleeClasses()
    {
        var mage = ClassDefinition.For(CharacterClass.Scientist);
        var wizard = ClassDefinition.For(CharacterClass.Engineer);
        var warrior = ClassDefinition.For(CharacterClass.Soldier);
        var thief = ClassDefinition.For(CharacterClass.Spy);

        Assert.True(mage.TachyonDrainMultiplier > warrior.TachyonDrainMultiplier);
        Assert.True(mage.TachyonDrainMultiplier > thief.TachyonDrainMultiplier);
        Assert.True(wizard.TachyonDrainMultiplier > warrior.TachyonDrainMultiplier);
        Assert.True(wizard.TachyonDrainMultiplier > thief.TachyonDrainMultiplier);
    }

    [Fact]
    public void MaxHpAtLevel_FullRateToTheKnee_ThenHalfRate()
    {
        var def = ClassDefinition.For(CharacterClass.Soldier);
        var knee = ClassDefinition.HpGrowthKneeLevel;

        Assert.Equal(def.BaseHp, def.MaxHpAtLevel(1));
        Assert.Equal(def.BaseHp + def.HpPerLevel * 4, def.MaxHpAtLevel(5)); // full rate below the knee

        var atKnee = def.BaseHp + def.HpPerLevel * (knee - 1);
        Assert.Equal(atKnee, def.MaxHpAtLevel(knee));
        // 10 levels past the knee add only half the usual HP.
        Assert.Equal(atKnee + def.HpPerLevel * 10 / 2, def.MaxHpAtLevel(knee + 10));
        Assert.True(def.MaxHpAtLevel(30) < def.BaseHp + def.HpPerLevel * 29,
            "past the knee the pool grows slower than the old flat-linear curve");
    }

    [Fact]
    public void MaxTachyonsAtLevel_GrowsLinearlyFromBase()
    {
        var def = ClassDefinition.For(CharacterClass.Scientist);

        Assert.Equal(def.BaseTachyons, def.MaxTachyonsAtLevel(1));
        Assert.Equal(def.BaseTachyons + def.TachyonsPerLevel * 9, def.MaxTachyonsAtLevel(10));
    }
}
