using Mutants.Core.Characters;
using Mutants.Core.Classes;
using Mutants.Engine.Combat;
using Mutants.Engine.Content;

namespace Mutants.Engine.Tests.Combat;

public class CombatSessionTests
{
    // v=0.5 keeps CombatResolver.RollDamage's variance factor at exactly
    // 1.0, so damage is deterministic: raw = attack - defense.
    private static StubRandomSource NeutralRandom() => StubRandomSource.Fixed(0.5);

    private static Mutant Warrior(string name = "Rook") => new(name, CharacterClass.Warrior);

    private static Core.Monsters.Monster TankMonster(string name = "Dummy", int hp = 200, int attack = 5, int defense = 2, int speed = 5, IReadOnlyList<string>? tags = null) =>
        new(name, tier: 1, maxHp: hp, attackPower: attack, defense: defense, speed: speed, xpReward: 40, tags: tags);

    private static AbilityData MakeAbility(
        string @class, int level, string name, string effect, double magnitude,
        int ionCost = 0, string condition = "", string? tag = null, int durationRounds = 0) => new()
    {
        Class = @class,
        Tier = 1,
        Level = level,
        Name = name,
        Description = "test ability",
        Effect = effect,
        Magnitude = magnitude,
        IonCost = ionCost,
        Condition = condition,
        Tag = tag,
        DurationRounds = durationRounds,
    };

    [Fact]
    public void Attack_DealsNormalDamage()
    {
        var mutant = Warrior();
        var monster = TankMonster();
        var session = new CombatSession(mutant, monster, NeutralRandom());

        session.Attack();

        // mutant Strength 15 - monster defense 2 = 13
        Assert.Equal(200 - 13, monster.Health.Current);
        Assert.Equal(1, session.Rounds);
    }

    [Fact]
    public void Attack_WhenAlreadyOver_DoesNothing()
    {
        var mutant = Warrior();
        var monster = TankMonster(hp: 1);
        monster.Health.Damage(1); // already dead
        var session = new CombatSession(mutant, monster, NeutralRandom());

        session.Attack();

        Assert.Equal(0, session.Rounds);
    }

    [Fact]
    public void Cast_WrongClass_FailsAndSpendsNoIons()
    {
        var mutant = Warrior();
        var startingIons = mutant.Ions.Current;
        var monster = TankMonster();
        var session = new CombatSession(mutant, monster, NeutralRandom());
        var mageAbility = MakeAbility("Mage", 5, "Firebolt", "Damage", 1.8, ionCost: 8);

        var result = session.Cast(mageAbility);

        Assert.False(result.Success);
        Assert.Equal(startingIons, mutant.Ions.Current);
        Assert.Equal(0, session.Rounds);
    }

    [Fact]
    public void Cast_BelowRequiredLevel_Fails()
    {
        var mutant = Warrior(); // level 1
        var monster = TankMonster();
        var session = new CombatSession(mutant, monster, NeutralRandom());
        var ability = MakeAbility("Warrior", 30, "Executioner", "Damage", 2.0, ionCost: 30);

        var result = session.Cast(ability);

        Assert.False(result.Success);
        Assert.Contains("level 30", result.Message);
    }

    [Fact]
    public void Cast_NoneEffect_RefusedWithNoIonsSpent()
    {
        var mutant = Warrior();
        mutant.LevelUp(); mutant.LevelUp(); mutant.LevelUp(); mutant.LevelUp(); // level 5
        var startingIons = mutant.Ions.Current;
        var monster = TankMonster();
        var session = new CombatSession(mutant, monster, NeutralRandom());
        var ability = MakeAbility("Warrior", 5, "Placeholder", "None", 0, ionCost: 5);

        var result = session.Cast(ability);

        Assert.False(result.Success);
        Assert.Equal(startingIons, mutant.Ions.Current);
        Assert.Equal(0, session.Rounds);
    }

    [Fact]
    public void Cast_InsufficientIons_Fails()
    {
        var mutant = Warrior();
        mutant.Ions.Spend(mutant.Ions.Current); // 0 Ions
        var monster = TankMonster();
        var session = new CombatSession(mutant, monster, NeutralRandom());
        var ability = MakeAbility("Warrior", 1, "Cleave", "Damage", 1.5, ionCost: 8);

        var result = session.Cast(ability);

        Assert.False(result.Success);
        Assert.Equal(0, session.Rounds);
    }

    [Fact]
    public void Cast_Damage_DealsMultipliedDamage()
    {
        var mutant = Warrior();
        var monster = TankMonster();
        var session = new CombatSession(mutant, monster, NeutralRandom());
        var ability = MakeAbility("Warrior", 1, "Cleave", "Damage", 1.5, ionCost: 8);

        var result = session.Cast(ability);

        Assert.True(result.Success);
        Assert.Equal(8, mutant.Ions.Max - mutant.Ions.Current); // spent
        Assert.Equal(200 - (int)Math.Round(13 * 1.5), monster.Health.Current);
    }

    [Fact]
    public void Cast_IgnoreDefenseDamage_IgnoresTargetDefense()
    {
        var mutant = Warrior();
        var monster = TankMonster();
        var session = new CombatSession(mutant, monster, NeutralRandom());
        var ability = MakeAbility("Warrior", 1, "Guard Break", "IgnoreDefenseDamage", 1.0, ionCost: 16);

        session.Cast(ability);

        Assert.Equal(200 - 15, monster.Health.Current); // full 15 attack, no defense subtracted
    }

    [Fact]
    public void Cast_Heal_RestoresFractionOfMaxHp()
    {
        var mutant = Warrior();
        mutant.Health.Damage(20); // 10/30
        var monster = TankMonster();
        var session = new CombatSession(mutant, monster, NeutralRandom());
        var ability = MakeAbility("Warrior", 1, "Second Wind", "Heal", 0.20, ionCost: 0);

        session.Cast(ability);

        // Casting a Heal still uses the round: the monster gets its own
        // counter-attack afterward, at least 1 damage by design.
        Assert.Equal(10 + (int)Math.Round(30 * 0.20) - 1, mutant.Health.Current);
    }

    [Fact]
    public void Cast_BuffSelfAttack_IncreasesFutureDamage()
    {
        var mutant = Warrior();
        var monster = TankMonster();
        var session = new CombatSession(mutant, monster, NeutralRandom());
        var buff = MakeAbility("Warrior", 1, "Rally", "BuffSelfAttack", 4, ionCost: 20);

        session.Cast(buff); // no damage this round - just sets the buff
        var hpAfterBuff = monster.Health.Current;
        session.Attack();

        var damageWithBuff = hpAfterBuff - monster.Health.Current;
        Assert.Equal(13 + 4, damageWithBuff);
    }

    [Fact]
    public void Cast_DebuffTargetDefense_IncreasesDamageDealt()
    {
        var mutant = Warrior();
        // Defense 10 (not the default 2) so a -6 debuff doesn't clip
        // against MonsterEffectiveDefense's floor of 0.
        var monster = TankMonster(defense: 10);
        var session = new CombatSession(mutant, monster, NeutralRandom());
        var debuff = MakeAbility("Warrior", 1, "Death Mark", "DebuffTargetDefense", 6, ionCost: 10);

        var castResult = session.Cast(debuff);
        Assert.True(castResult.Success);
        var hpAfterDebuff = monster.Health.Current;
        session.Attack();

        var damageAfterDebuff = hpAfterDebuff - monster.Health.Current;
        Assert.Equal(15 - (10 - 6), damageAfterDebuff); // attack 15 - (defense 10 debuffed by 6)
    }

    [Fact]
    public void Cast_ExtraAttack_HitsTwiceInOneRound()
    {
        var mutant = Warrior();
        var monster = TankMonster();
        var session = new CombatSession(mutant, monster, NeutralRandom());
        var ability = MakeAbility("Warrior", 1, "Shadow Step", "ExtraAttack", 1.0, ionCost: 20);

        session.Cast(ability);

        // two 13-damage hits, then the monster's own counter (still alive at 174 HP)
        Assert.Equal(200 - 13 - 13, monster.Health.Current);
    }

    [Fact]
    public void Cast_Shield_AbsorbsNextMonsterHit()
    {
        var mutant = Warrior();
        var startingHp = mutant.Health.Current;
        var monster = TankMonster();
        var session = new CombatSession(mutant, monster, NeutralRandom());
        var ability = MakeAbility("Warrior", 1, "Sanctuary", "Shield", 1, ionCost: 10);

        var castResult = session.Cast(ability); // sets the shield charge (no attack); monster's counter is then absorbed

        Assert.True(castResult.Success);
        Assert.Equal(startingHp, mutant.Health.Current);
    }

    [Fact]
    public void Cast_DamageOverTime_TicksOnSubsequentRounds()
    {
        var mutant = Warrior();
        var monster = TankMonster();
        var session = new CombatSession(mutant, monster, NeutralRandom());
        var ability = MakeAbility("Warrior", 1, "Poison Blade", "DamageOverTime", 6, ionCost: 16, durationRounds: 2);

        session.Cast(ability); // initial hit (13) + poison starts ticking
        var hpAfterCast = monster.Health.Current;
        Assert.Equal(200 - 13 - 6, hpAfterCast); // hit, then first DoT tick same round

        session.Attack(); // normal attack (13) + second DoT tick (6)
        Assert.Equal(hpAfterCast - 13 - 6, monster.Health.Current);

        var hpBeforeThirdRound = monster.Health.Current;
        session.Attack(); // DoT expired - no more ticks, just the attack
        Assert.Equal(hpBeforeThirdRound - 13, monster.Health.Current);
    }

    [Fact]
    public void Cast_GuaranteedCritNextAttack_BuffsTheFollowingHit()
    {
        var mutant = Warrior();
        var monster = TankMonster();
        var session = new CombatSession(mutant, monster, NeutralRandom());
        var vanish = MakeAbility("Warrior", 1, "Vanish", "GuaranteedCritNextAttack", 2.0, ionCost: 12);

        session.Cast(vanish); // sets up the crit, doesn't attack itself
        var hpAfterVanish = monster.Health.Current;
        session.Attack();

        var critDamage = hpAfterVanish - monster.Health.Current;
        Assert.Equal((int)Math.Round(13 * 2.0), critDamage);
    }

    [Fact]
    public void Cast_RestoreIons_AddsIonsBackWithoutNetLoss()
    {
        var mutant = Warrior();
        mutant.Ions.Spend(15); // 5/20 remaining
        var monster = TankMonster();
        var session = new CombatSession(mutant, monster, NeutralRandom());
        var ability = MakeAbility("Warrior", 1, "Mana Well", "RestoreIons", 0.25, ionCost: 0);

        session.Cast(ability);

        Assert.Equal(5 + (int)Math.Round(20 * 0.25), mutant.Ions.Current);
    }

    [Fact]
    public void Cast_InstantDefeatNonBoss_EndsTheFightImmediately()
    {
        var mutant = Warrior();
        var monster = TankMonster();
        var session = new CombatSession(mutant, monster, NeutralRandom(), allowBanish: true);
        var ability = MakeAbility("Warrior", 1, "Banish", "InstantDefeatNonBoss", 0, ionCost: 10);

        var result = session.Cast(ability);

        Assert.True(result.Success);
        Assert.True(monster.Health.IsDead);
        Assert.True(session.MutantWon);
    }

    [Fact]
    public void Cast_InstantDefeatNonBoss_RefusedWhenBanishNotAllowed()
    {
        var mutant = Warrior();
        var monster = TankMonster();
        var session = new CombatSession(mutant, monster, NeutralRandom(), allowBanish: false);
        var ability = MakeAbility("Warrior", 1, "Banish", "InstantDefeatNonBoss", 0, ionCost: 10);

        var result = session.Cast(ability);

        Assert.False(result.Success);
        Assert.False(monster.Health.IsDead);
    }

    [Fact]
    public void ConditionalDamage_AppliesBonusOnlyWhenConditionMet()
    {
        var mutant = Warrior();
        var undamagedTarget = TankMonster();
        var session = new CombatSession(mutant, undamagedTarget, NeutralRandom());
        var backstab = MakeAbility("Warrior", 1, "Backstab", "Damage", 2.0, ionCost: 8, condition: "TargetUndamaged");

        session.Cast(backstab);

        Assert.Equal(200 - (int)Math.Round(13 * 2.0), undamagedTarget.Health.Current);
    }

    [Fact]
    public void ConditionalDamage_FallsBackToNormalHitWhenConditionNotMet()
    {
        var mutant = Warrior();
        var damagedTarget = TankMonster();
        damagedTarget.Health.Damage(5); // no longer undamaged
        var session = new CombatSession(mutant, damagedTarget, NeutralRandom());
        var backstab = MakeAbility("Warrior", 1, "Backstab", "Damage", 2.0, ionCost: 8, condition: "TargetUndamaged");

        session.Cast(backstab);

        Assert.Equal(200 - 5 - 13, damagedTarget.Health.Current); // plain hit, no bonus
    }

    [Fact]
    public void ConditionalDamage_TagCondition_OnlyBonusesTaggedMonsters()
    {
        var undead = TankMonster(tags: ["undead"]);
        var turnUndead = MakeAbility("Priest", 15, "Turn Undead", "Damage", 2.5, ionCost: 16, condition: "TargetTagged", tag: "undead");
        var priest = new Mutant("Faye", CharacterClass.Priest);
        priest.LevelUp(); priest.LevelUp(); priest.LevelUp(); priest.LevelUp();
        priest.LevelUp(); priest.LevelUp(); priest.LevelUp(); priest.LevelUp();
        priest.LevelUp(); priest.LevelUp(); priest.LevelUp(); priest.LevelUp(); priest.LevelUp(); priest.LevelUp(); // level 15
        var priestSession = new CombatSession(priest, undead, NeutralRandom());

        priestSession.Cast(turnUndead);

        var priestAttack = priest.EffectiveAttackPower - undead.Defense;
        Assert.Equal(200 - (int)Math.Round(priestAttack * 2.5), undead.Health.Current);
    }

    [Fact]
    public void DebuffTargetSpeed_CanFlipTurnOrderStartingNextRound()
    {
        var mutant = new Mutant("Zeta", CharacterClass.Wizard); // base Agility 10
        var monster = TankMonster("Brute", hp: 500, speed: 11); // faster than the Wizard initially
        var session = new CombatSession(mutant, monster, NeutralRandom());
        var slow = MakeAbility("Wizard", 1, "Slow", "DebuffTargetSpeed", 5, ionCost: 8);

        session.Attack(); // round 1: monster still faster - monster's log line comes first
        Assert.StartsWith("Brute hits", session.Log[0]);
        Assert.StartsWith("Zeta hits", session.Log[1]);

        session.Cast(slow); // round 2: monster still acts first this round (order set before the cast resolves)
        Assert.StartsWith("Brute hits", session.Log[2]);

        session.Attack(); // round 3: monster's effective speed is now 11-5=6 < Wizard's 10 - Wizard acts first
        Assert.StartsWith("Zeta hits", session.Log[^2]);
    }

    [Fact]
    public void Cast_VictoryAwardsXpAndLoot()
    {
        var mutant = Warrior();
        var monster = TankMonster(hp: 5); // one big hit finishes it
        var session = new CombatSession(mutant, monster, NeutralRandom());
        var ability = MakeAbility("Warrior", 1, "Cleave", "Damage", 1.5, ionCost: 8);

        session.Cast(ability);

        Assert.True(session.IsOver);
        Assert.True(session.MutantWon);
        Assert.Equal(monster.XpReward, session.XpAwarded);
        Assert.True(mutant.Xp >= monster.XpReward);
    }

    [Fact]
    public void Loss_AwardsNothing()
    {
        var mutant = Warrior();
        mutant.Health.Damage(mutant.Health.Max - 1); // 1 HP
        var monster = TankMonster(attack: 1000, speed: 999); // one-shots the mutant, acts first
        var session = new CombatSession(mutant, monster, NeutralRandom());

        session.Attack();

        Assert.True(session.IsOver);
        Assert.False(session.MutantWon);
        Assert.Equal(0, session.XpAwarded);
        Assert.Empty(session.ItemsDropped);
    }
}
