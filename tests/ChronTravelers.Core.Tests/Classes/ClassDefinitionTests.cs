using ChronTravelers.Core.Classes;

namespace ChronTravelers.Core.Tests.Classes;

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
    public void ArcaneClasses_DrainIonsFasterThanMeleeClasses()
    {
        var mage = ClassDefinition.For(CharacterClass.Scientist);
        var wizard = ClassDefinition.For(CharacterClass.Engineer);
        var warrior = ClassDefinition.For(CharacterClass.Soldier);
        var thief = ClassDefinition.For(CharacterClass.Spy);

        Assert.True(mage.IonDrainMultiplier > warrior.IonDrainMultiplier);
        Assert.True(mage.IonDrainMultiplier > thief.IonDrainMultiplier);
        Assert.True(wizard.IonDrainMultiplier > warrior.IonDrainMultiplier);
        Assert.True(wizard.IonDrainMultiplier > thief.IonDrainMultiplier);
    }

    [Fact]
    public void MaxHpAtLevel_GrowsLinearlyFromBase()
    {
        var def = ClassDefinition.For(CharacterClass.Soldier);

        Assert.Equal(def.BaseHp, def.MaxHpAtLevel(1));
        Assert.Equal(def.BaseHp + def.HpPerLevel * 4, def.MaxHpAtLevel(5));
    }

    [Fact]
    public void MaxIonsAtLevel_GrowsLinearlyFromBase()
    {
        var def = ClassDefinition.For(CharacterClass.Scientist);

        Assert.Equal(def.BaseIons, def.MaxIonsAtLevel(1));
        Assert.Equal(def.BaseIons + def.IonsPerLevel * 9, def.MaxIonsAtLevel(10));
    }
}
