using Mutants.Core.Stats;

namespace Mutants.Core.Classes;

/// <summary>
/// Per-class growth/scaling constants. The class roster, roles, and primary
/// stats are per docs/GDD.md §4 (partially [SOURCE]); the specific numeric
/// values here (base HP/Ions, per-level growth, Ion drain multiplier) are
/// NOT in the GDD — they are original placeholder tuning filling the gap
/// noted in docs/GDD.md §4.3 ("Ion pools and drain rates differ per class").
/// Per docs/AGENTS.md, these are exactly the kind of tunable numbers that
/// should move into a Mutants.Content data file (and get Design Agent
/// sign-off) once that project exists — flagged here rather than hidden.
/// </summary>
public sealed record ClassDefinition(
    CharacterClass Class,
    PrimaryStat PrimaryStat,
    StatBlock BaseStats,
    int BaseHp,
    int HpPerLevel,
    int BaseIons,
    int IonsPerLevel,
    double IonDrainMultiplier)
{
    /// <summary>All five class definitions, keyed by <see cref="CharacterClass"/>.</summary>
    public static readonly IReadOnlyDictionary<CharacterClass, ClassDefinition> All =
        new Dictionary<CharacterClass, ClassDefinition>
        {
            [CharacterClass.Warrior] = new(
                CharacterClass.Warrior, PrimaryStat.Strength,
                BaseStats: new StatBlock(Strength: 15, Agility: 10, Faith: 8, Intellect: 8),
                BaseHp: 30, HpPerLevel: 6,
                BaseIons: 20, IonsPerLevel: 2,
                IonDrainMultiplier: 0.8),

            [CharacterClass.Thief] = new(
                CharacterClass.Thief, PrimaryStat.Agility,
                BaseStats: new StatBlock(Strength: 9, Agility: 15, Faith: 8, Intellect: 10),
                BaseHp: 24, HpPerLevel: 5,
                BaseIons: 24, IonsPerLevel: 2,
                IonDrainMultiplier: 0.9),

            [CharacterClass.Priest] = new(
                CharacterClass.Priest, PrimaryStat.Faith,
                BaseStats: new StatBlock(Strength: 9, Agility: 8, Faith: 15, Intellect: 10),
                BaseHp: 22, HpPerLevel: 4,
                BaseIons: 30, IonsPerLevel: 3,
                IonDrainMultiplier: 1.0),

            [CharacterClass.Mage] = new(
                CharacterClass.Mage, PrimaryStat.Intellect,
                BaseStats: new StatBlock(Strength: 7, Agility: 9, Faith: 8, Intellect: 16),
                BaseHp: 18, HpPerLevel: 3,
                BaseIons: 34, IonsPerLevel: 4,
                IonDrainMultiplier: 1.3),

            [CharacterClass.Wizard] = new(
                CharacterClass.Wizard, PrimaryStat.Intellect,
                BaseStats: new StatBlock(Strength: 7, Agility: 10, Faith: 9, Intellect: 15),
                BaseHp: 18, HpPerLevel: 3,
                BaseIons: 32, IonsPerLevel: 4,
                IonDrainMultiplier: 1.2),
        };

    public static ClassDefinition For(CharacterClass characterClass) => All[characterClass];

    public int MaxHpAtLevel(int level) => BaseHp + HpPerLevel * (level - 1);

    public int MaxIonsAtLevel(int level) => BaseIons + IonsPerLevel * (level - 1);
}
