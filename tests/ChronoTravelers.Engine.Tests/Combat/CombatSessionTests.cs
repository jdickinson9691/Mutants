using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Monsters;
using ChronoTravelers.Engine.Combat;
using ChronoTravelers.Engine.Content;

namespace ChronoTravelers.Engine.Tests.Combat;

public class CombatSessionTests
{
    // v=0.5 keeps CombatResolver.RollDamage's variance factor at exactly
    // 1.0, so damage is deterministic: raw = attack - defense.
    private static StubRandomSource NeutralRandom() => StubRandomSource.Fixed(0.5);

    private static Traveler Soldier(string name = "Rook") => new(name, CharacterClass.Soldier);

    private static Core.Monsters.Monster TankMonster(string name = "Dummy", int hp = 200, int attack = 5, int defense = 2, int speed = 5, IReadOnlyList<string>? tags = null) =>
        new(name, tier: 1, maxHp: hp, attackPower: attack, defense: defense, speed: speed, xpReward: 40, tags: tags);

    private static AbilityData MakeAbility(
        string @class, int level, string name, string effect, double magnitude,
        int tachyonCost = 0, string condition = "", string? tag = null, int durationRounds = 0) => new()
    {
        Class = @class,
        Tier = 1,
        Level = level,
        Name = name,
        Description = "test ability",
        Effect = effect,
        Magnitude = magnitude,
        TachyonCost = tachyonCost,
        Condition = condition,
        Tag = tag,
        DurationRounds = durationRounds,
    };

    [Fact]
    public void Attack_DealsNormalDamage()
    {
        var traveler = Soldier();
        var monster = TankMonster();
        var session = new CombatSession(traveler, monster, NeutralRandom());

        session.Attack();

        // traveler Strength 15 - monster defense 2 = 13
        Assert.Equal(200 - 13, monster.Health.Current);
        Assert.Equal(1, session.Rounds);
    }

    [Fact]
    public void Attack_WhenAlreadyOver_DoesNothing()
    {
        var traveler = Soldier();
        var monster = TankMonster(hp: 1);
        monster.Health.Damage(1); // already dead
        var session = new CombatSession(traveler, monster, NeutralRandom());

        session.Attack();

        Assert.Equal(0, session.Rounds);
    }

    [Fact]
    public void Cast_WrongClass_FailsAndSpendsNoTachyons()
    {
        var traveler = Soldier();
        var startingTachyons = traveler.Tachyons.Current;
        var monster = TankMonster();
        var session = new CombatSession(traveler, monster, NeutralRandom());
        var mageAbility = MakeAbility("Scientist", 5, "Firebolt", "Damage", 1.8, tachyonCost: 8);

        var result = session.Cast(mageAbility);

        Assert.False(result.Success);
        Assert.Equal(startingTachyons, traveler.Tachyons.Current);
        Assert.Equal(0, session.Rounds);
    }

    [Fact]
    public void Cast_BelowRequiredLevel_Fails()
    {
        var traveler = Soldier(); // level 1
        var monster = TankMonster();
        var session = new CombatSession(traveler, monster, NeutralRandom());
        var ability = MakeAbility("Soldier", 30, "Executioner", "Damage", 2.0, tachyonCost: 30);

        var result = session.Cast(ability);

        Assert.False(result.Success);
        Assert.Contains("level 30", result.Message);
    }

    [Fact]
    public void Cast_NoneEffect_RefusedWithNoTachyonsSpent()
    {
        var traveler = Soldier();
        traveler.LevelUp(); traveler.LevelUp(); traveler.LevelUp(); traveler.LevelUp(); // level 5
        var startingTachyons = traveler.Tachyons.Current;
        var monster = TankMonster();
        var session = new CombatSession(traveler, monster, NeutralRandom());
        var ability = MakeAbility("Soldier", 5, "Placeholder", "None", 0, tachyonCost: 5);

        var result = session.Cast(ability);

        Assert.False(result.Success);
        Assert.Equal(startingTachyons, traveler.Tachyons.Current);
        Assert.Equal(0, session.Rounds);
    }

    [Fact]
    public void Cast_InsufficientTachyons_Fails()
    {
        var traveler = Soldier();
        traveler.Tachyons.Spend(traveler.Tachyons.Current); // 0 Tachyons
        var monster = TankMonster();
        var session = new CombatSession(traveler, monster, NeutralRandom());
        var ability = MakeAbility("Soldier", 1, "Cleave", "Damage", 1.5, tachyonCost: 8);

        var result = session.Cast(ability);

        Assert.False(result.Success);
        Assert.Equal(0, session.Rounds);
    }

    [Fact]
    public void Cast_Damage_DealsMultipliedDamage()
    {
        var traveler = Soldier();
        var monster = TankMonster();
        var session = new CombatSession(traveler, monster, NeutralRandom());
        var ability = MakeAbility("Soldier", 1, "Cleave", "Damage", 1.5, tachyonCost: 8);

        var result = session.Cast(ability);

        Assert.True(result.Success);
        Assert.Equal(8, traveler.Tachyons.Max - traveler.Tachyons.Current); // spent
        Assert.Equal(200 - (int)Math.Round(13 * 1.5), monster.Health.Current);
    }

    [Fact]
    public void Cast_IgnoreDefenseDamage_IgnoresTargetDefense()
    {
        var traveler = Soldier();
        var monster = TankMonster();
        var session = new CombatSession(traveler, monster, NeutralRandom());
        var ability = MakeAbility("Soldier", 1, "Guard Break", "IgnoreDefenseDamage", 1.0, tachyonCost: 16);

        session.Cast(ability);

        Assert.Equal(200 - 15, monster.Health.Current); // full 15 attack, no defense subtracted
    }

    [Fact]
    public void Attack_ArmourPenFloor_HeavyArmourStillTakesRealDamage()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        var plate = Item.Create("Bulwark Plate", ItemType.Armor, tier: 6, Rarity.Rare, CharacterClass.Soldier);
        traveler.AddToInventory(plate);
        traveler.Wield(plate);
        Assert.True(traveler.EffectiveDefense > 30); // far above the monster's attack — pre-floor this was a 1-damage hit

        var monster = new Monster("Sledge", tier: 1, maxHp: 500, attackPower: 30, defense: 0, speed: 1, xpReward: 10);
        var hpBefore = traveler.Health.Current;

        new CombatSession(traveler, monster, NeutralRandom()).Attack(); // one round incl. the monster's counter

        var taken = hpBefore - traveler.Health.Current;
        Assert.True(taken >= 7, $"armour-pen floor should land ~0.30 × 30 ≈ 9, not 1; took {taken}");
    }

    [Fact]
    public void Cast_Heal_RestoresFractionOfMaxHp()
    {
        var traveler = Soldier();
        traveler.Health.Damage(20); // 10/30
        var monster = TankMonster();
        var session = new CombatSession(traveler, monster, NeutralRandom());
        var ability = MakeAbility("Soldier", 1, "Second Wind", "Heal", 0.20, tachyonCost: 0);

        session.Cast(ability);

        // Casting a Heal still uses the round: the monster gets its own
        // counter-attack afterward. TankMonster attack 5 vs Soldier
        // defense 5 → raw 0, but the armour-pen floor (0.35 × 5 ≈ 2) lands
        // it for 2.
        Assert.Equal(10 + (int)Math.Round(30 * 0.20) - 2, traveler.Health.Current);
    }

    [Fact]
    public void Cast_BuffSelfAttack_IncreasesFutureDamage()
    {
        var traveler = Soldier();
        var monster = TankMonster();
        var session = new CombatSession(traveler, monster, NeutralRandom());
        var buff = MakeAbility("Soldier", 1, "Rally", "BuffSelfAttack", 4, tachyonCost: 20);

        session.Cast(buff); // no damage this round - just sets the buff
        var hpAfterBuff = monster.Health.Current;
        session.Attack();

        var damageWithBuff = hpAfterBuff - monster.Health.Current;
        Assert.Equal(13 + 4, damageWithBuff);
    }

    [Fact]
    public void Cast_DebuffTargetDefense_IncreasesDamageDealt()
    {
        var traveler = Soldier();
        // Defense 10 (not the default 2) so a -6 debuff doesn't clip
        // against MonsterEffectiveDefense's floor of 0.
        var monster = TankMonster(defense: 10);
        var session = new CombatSession(traveler, monster, NeutralRandom());
        var debuff = MakeAbility("Soldier", 1, "Death Mark", "DebuffTargetDefense", 6, tachyonCost: 10);

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
        var traveler = Soldier();
        var monster = TankMonster();
        var session = new CombatSession(traveler, monster, NeutralRandom());
        var ability = MakeAbility("Soldier", 1, "Shadow Step", "ExtraAttack", 1.0, tachyonCost: 20);

        session.Cast(ability);

        // two 13-damage hits, then the monster's own counter (still alive at 174 HP)
        Assert.Equal(200 - 13 - 13, monster.Health.Current);
    }

    [Fact]
    public void Cast_Shield_AbsorbsNextMonsterHit()
    {
        var traveler = Soldier();
        var startingHp = traveler.Health.Current;
        var monster = TankMonster();
        var session = new CombatSession(traveler, monster, NeutralRandom());
        var ability = MakeAbility("Soldier", 1, "Sanctuary", "Shield", 1, tachyonCost: 10);

        var castResult = session.Cast(ability); // sets the shield charge (no attack); monster's counter is then absorbed

        Assert.True(castResult.Success);
        Assert.Equal(startingHp, traveler.Health.Current);
    }

    [Fact]
    public void Cast_DamageOverTime_TicksOnSubsequentRounds()
    {
        var traveler = Soldier();
        var monster = TankMonster();
        var session = new CombatSession(traveler, monster, NeutralRandom());
        var ability = MakeAbility("Soldier", 1, "Poison Blade", "DamageOverTime", 6, tachyonCost: 16, durationRounds: 2);

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
        var traveler = Soldier();
        var monster = TankMonster();
        var session = new CombatSession(traveler, monster, NeutralRandom());
        var vanish = MakeAbility("Soldier", 1, "Vanish", "GuaranteedCritNextAttack", 2.0, tachyonCost: 12);

        session.Cast(vanish); // sets up the crit, doesn't attack itself
        var hpAfterVanish = monster.Health.Current;
        session.Attack();

        var critDamage = hpAfterVanish - monster.Health.Current;
        Assert.Equal((int)Math.Round(13 * 2.0), critDamage);
    }

    [Fact]
    public void Cast_RestoreTachyons_AddsTachyonsBackWithoutNetLoss()
    {
        var traveler = Soldier();
        var max = traveler.Tachyons.Max;
        traveler.Tachyons.Spend(15);
        var before = traveler.Tachyons.Current;
        var monster = TankMonster();
        var session = new CombatSession(traveler, monster, NeutralRandom());
        var ability = MakeAbility("Soldier", 1, "Mana Well", "RestoreTachyons", 0.25, tachyonCost: 0);

        session.Cast(ability);

        // RestoreTachyons adds round(nominalMax * magnitude); the player pool
        // is uncapped so nothing clamps the result here.
        Assert.Equal(before + (int)Math.Round(max * 0.25), traveler.Tachyons.Current);
    }

    [Fact]
    public void Cast_InstantDefeatNonBoss_EndsTheFightImmediately()
    {
        var traveler = Soldier();
        var monster = TankMonster();
        var session = new CombatSession(traveler, monster, NeutralRandom(), allowBanish: true);
        var ability = MakeAbility("Soldier", 1, "Banish", "InstantDefeatNonBoss", 0, tachyonCost: 10);

        var result = session.Cast(ability);

        Assert.True(result.Success);
        Assert.True(monster.Health.IsDead);
        Assert.True(session.TravelerWon);
    }

    [Fact]
    public void Cast_InstantDefeatNonBoss_RefusedWhenBanishNotAllowed()
    {
        var traveler = Soldier();
        var monster = TankMonster();
        var session = new CombatSession(traveler, monster, NeutralRandom(), allowBanish: false);
        var ability = MakeAbility("Soldier", 1, "Banish", "InstantDefeatNonBoss", 0, tachyonCost: 10);

        var result = session.Cast(ability);

        Assert.False(result.Success);
        Assert.False(monster.Health.IsDead);
    }

    [Fact]
    public void ConditionalDamage_AppliesBonusOnlyWhenConditionMet()
    {
        var traveler = Soldier();
        var undamagedTarget = TankMonster();
        var session = new CombatSession(traveler, undamagedTarget, NeutralRandom());
        var backstab = MakeAbility("Soldier", 1, "Backstab", "Damage", 2.0, tachyonCost: 8, condition: "TargetUndamaged");

        session.Cast(backstab);

        Assert.Equal(200 - (int)Math.Round(13 * 2.0), undamagedTarget.Health.Current);
    }

    [Fact]
    public void ConditionalDamage_FallsBackToNormalHitWhenConditionNotMet()
    {
        var traveler = Soldier();
        var damagedTarget = TankMonster();
        damagedTarget.Health.Damage(5); // no longer undamaged
        var session = new CombatSession(traveler, damagedTarget, NeutralRandom());
        var backstab = MakeAbility("Soldier", 1, "Backstab", "Damage", 2.0, tachyonCost: 8, condition: "TargetUndamaged");

        session.Cast(backstab);

        Assert.Equal(200 - 5 - 13, damagedTarget.Health.Current); // plain hit, no bonus
    }

    [Fact]
    public void ConditionalDamage_TagCondition_OnlyBonusesTaggedMonsters()
    {
        var undead = TankMonster(tags: ["undead"], hp: 5000); // big pool so a level-15 caster's 2.5x hit doesn't just kill it
        var turnUndead = MakeAbility("Doctor", 15, "Turn Undead", "Damage", 2.5, tachyonCost: 16, condition: "TargetTagged", tag: "undead");
        var priest = new Traveler("Faye", CharacterClass.Doctor);
        priest.LevelUp(); priest.LevelUp(); priest.LevelUp(); priest.LevelUp();
        priest.LevelUp(); priest.LevelUp(); priest.LevelUp(); priest.LevelUp();
        priest.LevelUp(); priest.LevelUp(); priest.LevelUp(); priest.LevelUp(); priest.LevelUp(); priest.LevelUp(); // level 15
        var priestSession = new CombatSession(priest, undead, NeutralRandom());

        priestSession.Cast(turnUndead);

        var priestAttack = priest.EffectiveAttackPower - undead.Defense;
        Assert.Equal(5000 - (int)Math.Round(priestAttack * 2.5), undead.Health.Current);
    }

    [Fact]
    public void DebuffTargetSpeed_CanFlipTurnOrderStartingNextRound()
    {
        var traveler = new Traveler("Zeta", CharacterClass.Engineer); // base Agility 10
        var monster = TankMonster("Brute", hp: 500, speed: 11); // faster than the Engineer initially
        var session = new CombatSession(traveler, monster, NeutralRandom());
        var slow = MakeAbility("Engineer", 1, "Slow", "DebuffTargetSpeed", 5, tachyonCost: 8);

        session.Attack(); // round 1: monster still faster - monster's log line comes first
        Assert.StartsWith("Brute hits", session.Log[0]);
        Assert.StartsWith("Zeta hits", session.Log[1]);

        session.Cast(slow); // round 2: monster still acts first this round (order set before the cast resolves)
        Assert.StartsWith("Brute hits", session.Log[2]);

        session.Attack(); // round 3: monster's effective speed is now 11-5=6 < Engineer's 10 - Engineer acts first
        Assert.StartsWith("Zeta hits", session.Log[^2]);
    }

    [Fact]
    public void Cast_VictoryAwardsXpAndLoot()
    {
        var traveler = Soldier();
        var monster = TankMonster(hp: 5); // one big hit finishes it
        var session = new CombatSession(traveler, monster, NeutralRandom());
        var ability = MakeAbility("Soldier", 1, "Cleave", "Damage", 1.5, tachyonCost: 8);

        session.Cast(ability);

        Assert.True(session.IsOver);
        Assert.True(session.TravelerWon);
        Assert.Equal(monster.XpReward, session.XpAwarded);
        Assert.True(traveler.Xp >= monster.XpReward);
    }

    [Fact]
    public void Victory_DoesNotPutLootInThePlayersPack_ItsForTheCallerToGround()
    {
        var traveler = Soldier();
        var inventoryBefore = traveler.Inventory.Count;
        var monster = TankMonster(hp: 5);
        var session = new CombatSession(traveler, monster, NeutralRandom());

        session.Attack();

        Assert.True(session.TravelerWon);
        Assert.NotEmpty(session.ItemsDropped); // RollForKill always yields something
        Assert.Equal(inventoryBefore, traveler.Inventory.Count); // ...but none of it auto-acquired
    }

    [Fact]
    public void Loss_AwardsNothing()
    {
        var traveler = Soldier();
        traveler.Health.Damage(traveler.Health.Max - 1); // 1 HP
        var monster = TankMonster(attack: 1000, speed: 999); // one-shots the traveler, acts first
        var session = new CombatSession(traveler, monster, NeutralRandom());

        session.Attack();

        Assert.True(session.IsOver);
        Assert.False(session.TravelerWon);
        Assert.Equal(0, session.XpAwarded);
        Assert.Empty(session.ItemsDropped);
    }
}
