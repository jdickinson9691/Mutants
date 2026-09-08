using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Engine.Combat;
using ChronoTravelers.Engine.Content;

namespace ChronoTravelers.Engine.Tests.Combat;

/// <summary>
/// Coverage for the beta-review follow-up that PvP (docs/GDD.md §11) was
/// melee/basic-attack only on both sides — see PvpAbilityCombat's own doc
/// comment for the design. StubRandomSource.Fixed(0.5) keeps
/// CombatResolver.RollDamage's variance factor at exactly 1.0 (same trick
/// CombatSessionTests/RangedResolverTests use) and, since PvpAbilityCombat's
/// AbilityCastChance is 0.6, also keeps every "should I bother casting"
/// roll a guaranteed yes (0.5 &lt; 0.6) without needing a scripted sequence.
/// </summary>
public class PvpAbilityCombatTests
{
    private static StubRandomSource NeutralRandom() => StubRandomSource.Fixed(0.5);

    private static Traveler Soldier(string name = "Rook") => new(name, CharacterClass.Soldier);

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
    public void Fight_WithNoAbilitiesEitherSide_ResolvesWithBasicAttacksOnly()
    {
        var attacker = Soldier("Ada");
        var defender = Soldier("Bo");
        // Equal speed means the attacker acts first (ties favor "self" in
        // PvpAbilityCombat's turn order, same convention CombatResolver.Fight
        // uses) — one HP means that first basic attack ends it, so this
        // stays deterministic without needing to out-level Bo.
        defender.Health.Damage(defender.Health.Max - 1);

        var result = PvpAbilityCombat.Fight(attacker, defender, abilities: [], NeutralRandom());

        Assert.True(result.AttackerWon);
        Assert.Equal(1, result.Rounds);
        Assert.DoesNotContain(result.Log, l => l.Contains("casts"));
    }

    [Fact]
    public void Fight_AttackerHasAnAffordableAbility_CastsItAndSpendsTachyons()
    {
        var attacker = Soldier("Ada");
        var defender = Soldier("Bo");
        attacker.Tachyons.Add(100);
        var tachyonsBefore = attacker.Tachyons.Current;

        var abilities = new List<AbilityData>
        {
            MakeAbility("Soldier", level: 1, "Power Strike", "Damage", magnitude: 1.5, tachyonCost: 5),
        };

        var result = PvpAbilityCombat.Fight(attacker, defender, abilities, NeutralRandom());

        Assert.Contains(result.Log, l => l.Contains("casts Power Strike"));
        Assert.True(attacker.Tachyons.Current < tachyonsBefore);
    }

    [Fact]
    public void Fight_OnlyAbilityIsInstantDefeat_NeverBanishesTheOpponent()
    {
        var attacker = Soldier("Ada");
        var defender = Soldier("Bo");
        attacker.Tachyons.Add(100);

        var abilities = new List<AbilityData>
        {
            MakeAbility("Soldier", level: 1, "Banish", "InstantDefeatNonBoss", magnitude: 0, tachyonCost: 0),
        };

        var result = PvpAbilityCombat.Fight(attacker, defender, abilities, NeutralRandom());

        // Excluded from the usable set entirely (see PvpAbilityCombat's doc
        // comment) — the fight takes real rounds of basic attacks instead
        // of ending in one.
        Assert.DoesNotContain(result.Log, l => l.Contains("casts"));
        Assert.True(result.Rounds > 1);
    }

    [Fact]
    public void Fight_BothSidesReadyARangedWeapon_FiresOneOpeningShotEach()
    {
        var attacker = Soldier("Ada");
        var defender = Soldier("Bo");
        // High HP on both so an opening shot alone can't end the fight
        // before the melee/ability exchange even starts.
        attacker.Health.Heal(100_000);
        defender.Health.Heal(100_000);

        var attackerBow = Item.CreateRanged("Longbow", 2, Rarity.Uncommon, RangedKind.Bow, ammoCapacity: 10);
        var defenderBow = Item.CreateRanged("Shortbow", 2, Rarity.Uncommon, RangedKind.Bow, ammoCapacity: 10);
        attacker.AddToInventory(attackerBow);
        attacker.Wield(attackerBow);
        defender.AddToInventory(defenderBow);
        defender.Wield(defenderBow);

        var result = PvpAbilityCombat.Fight(attacker, defender, abilities: [], NeutralRandom());

        Assert.Equal(9, attackerBow.AmmoRemaining);
        Assert.Equal(9, defenderBow.AmmoRemaining);
        Assert.Contains(result.Log, l => l.Contains(attackerBow.Name));
        Assert.Contains(result.Log, l => l.Contains(defenderBow.Name));
    }

    // --- ability-casting AI (battery-test tuning pass, 2026-09-08) ---

    [Fact]
    public void Fight_RepeatStreakPenalty_EventuallyLetsALowerScoringAbilityWin()
    {
        var attacker = Soldier("Ada");
        var defender = Soldier("Bo");
        attacker.Tachyons.Add(100_000);
        // Huge HP pools on both sides (Heal alone clamps to Max, so raise
        // Max first) so neither drops before the repeat-streak penalty has
        // had several rounds to shift the pick off "Strong".
        attacker.Health.SetMax(1_000_000);
        attacker.Health.Heal(1_000_000);
        defender.Health.SetMax(1_000_000);
        defender.Health.Heal(1_000_000);

        // "Strong" (score 9 + 20 = 29) always beats "Weak" (score 4) on a
        // bare comparison — before the repeat-streak penalty, "Weak" would
        // never appear in the log at all, the same shape as the
        // battery-test finding that Spy's Nerve Agent crowded out the rest
        // of its kit.
        var abilities = new List<AbilityData>
        {
            MakeAbility("Soldier", level: 1, "Strong", "Damage", magnitude: 20, tachyonCost: 1),
            MakeAbility("Soldier", level: 1, "Weak", "BuffSelfDefense", magnitude: 1),
        };

        var result = PvpAbilityCombat.Fight(attacker, defender, abilities, NeutralRandom());

        Assert.Contains(result.Log, l => l.Contains("bolstered")); // "Weak" (BuffSelfDefense)'s log line
    }

    [Fact]
    public void Fight_RestoreTachyonsAbility_CanCastWellBeforeThePoolIsNearlyEmpty()
    {
        var attacker = Soldier("Ada");
        var defender = Soldier("Bo");
        defender.Health.Heal(1_000_000);
        attacker.Tachyons.SetMax(1000);
        attacker.Tachyons.Add(1000);
        attacker.Tachyons.Spend(300); // 70% full — well above the old, effectively-unreachable "below 30% of max" gate

        // Charge-Siphon-shaped: free to cast, restores a fraction of max.
        var abilities = new List<AbilityData>
        {
            MakeAbility("Soldier", level: 1, "Charge Siphon", "RestoreTachyons", magnitude: 0.25),
        };

        var result = PvpAbilityCombat.Fight(attacker, defender, abilities, NeutralRandom());

        Assert.Contains(result.Log, l => l.Contains("restores")); // RestoreTachyons' log line
    }
}
