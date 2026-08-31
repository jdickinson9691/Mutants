using ChronTravelers.Core.Classes;

namespace ChronTravelers.Core.Tests.Classes;

public class ClassDefinitionTests
{
    [Fact]
    public void All_ContainsExactlyTheFiveSourcedClasses()
    {
        var expected = new[]
        {
            CharacterClass.Warrior, CharacterClass.Thief, CharacterClass.Priest,
            CharacterClass.Mage, CharacterClass.Wizard,
        };

        Assert.Equal(expected.Length, ClassDefinition.All.Count);
        foreach (var characterClass in expected)
        {
            Assert.True(ClassDefinition.All.ContainsKey(characterClass));
        }
    }

    [Theory]
    [InlineData(CharacterClass.Warrior, PrimaryStat.Strength)]
    [InlineData(CharacterClass.Thief, PrimaryStat.Agility)]
    [InlineData(CharacterClass.Priest, PrimaryStat.Faith)]
    [InlineData(CharacterClass.Mage, PrimaryStat.Intellect)]
    [InlineData(CharacterClass.Wizard, PrimaryStat.Intellect)]
    public void PrimaryStat_MatchesGddTable(CharacterClass characterClass, PrimaryStat expected)
    {
        Assert.Equal(expected, ClassDefinition.For(characterClass).PrimaryStat);
    }

    [Fact]
    public void Warrior_HasHighestBaseAndPerLevelHp()
    {
        var warriorHp = ClassDefinition.For(CharacterClass.Warrior).BaseHp;
        foreach (var other in ClassDefinition.All.Values.Where(d => d.Class != CharacterClass.Warrior))
        {
            Assert.True(warriorHp >= other.BaseHp,
                $"Warrior base HP ({warriorHp}) should be >= {other.Class} ({other.BaseHp}) per GDD §4 'best HP'.");
        }
    }

    [Fact]
    public void ArcaneClasses_DrainIonsFasterThanMeleeClasses()
    {
        var mage = ClassDefinition.For(CharacterClass.Mage);
        var wizard = ClassDefinition.For(CharacterClass.Wizard);
        var warrior = ClassDefinition.For(CharacterClass.Warrior);
        var thief = ClassDefinition.For(CharacterClass.Thief);

        Assert.True(mage.IonDrainMultiplier > warrior.IonDrainMultiplier);
        Assert.True(mage.IonDrainMultiplier > thief.IonDrainMultiplier);
        Assert.True(wizard.IonDrainMultiplier > warrior.IonDrainMultiplier);
        Assert.True(wizard.IonDrainMultiplier > thief.IonDrainMultiplier);
    }

    [Fact]
    public void MaxHpAtLevel_GrowsLinearlyFromBase()
    {
        var def = ClassDefinition.For(CharacterClass.Warrior);

        Assert.Equal(def.BaseHp, def.MaxHpAtLevel(1));
        Assert.Equal(def.BaseHp + def.HpPerLevel * 4, def.MaxHpAtLevel(5));
    }

    [Fact]
    public void MaxIonsAtLevel_GrowsLinearlyFromBase()
    {
        var def = ClassDefinition.For(CharacterClass.Mage);

        Assert.Equal(def.BaseIons, def.MaxIonsAtLevel(1));
        Assert.Equal(def.BaseIons + def.IonsPerLevel * 9, def.MaxIonsAtLevel(10));
    }
}
