using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Traits;
using ChronoTravelers.Engine.Npc;

namespace ChronoTravelers.PlaytestHarness;

/// <summary>Cast attempts against one named ability, for one run.</summary>
public sealed class AbilityUsage
{
    public int Attempts { get; set; }
    public int Successes { get; set; }
    public int Failures { get; set; }
}

/// <summary>
/// Observed activations of one <see cref="PassiveHook"/>, for one run — see
/// ChronoTravelers.Core.Diagnostics.PassiveActivationTracker. <c>TotalMagnitude</c>
/// is in whatever concrete unit that hook records (HP, Credits, Tachyons,
/// attack points — a raw count of 1.0 per activation for a hook with no
/// natural unit, e.g. a dodge roll).
/// </summary>
public sealed class PassiveUsage
{
    public int Activations { get; set; }
    public double TotalMagnitude { get; set; }
}

/// <summary>
/// Uses of one <see cref="ConsumableEffectType"/>, for one run, split by
/// whether it happened mid-fight (<see cref="ChronoTravelers.Engine.Combat.CombatSession.UseItem"/>,
/// spending the round the way an attack/cast would) or between fights
/// (<c>PlaytestRunner.TryHealOrConsume</c>'s plain <c>Traveler.Consume</c>).
/// </summary>
public sealed class ConsumableUsage
{
    public int InCombat { get; set; }
    public int OutOfCombat { get; set; }
}

/// <summary>
/// One NPC's final state at the end of a run, tagged by its trait at that
/// point (a respawned NPC rerolls, so this is "whatever it currently is,"
/// not its full-run history) — the raw material for comparing a trait's
/// actual effect against the None baseline rather than just how often it
/// spawns. <c>OwnsStore</c> is true if it holds any store slot across any
/// year the world has visited by run's end. <c>KillCount</c> is scoped the
/// same way as everything else here — kills since this NPC's current
/// incarnation spawned/respawned, via WorldSimulation.OnNpcAct — added
/// specifically to check whether a trait's Credits/Level gap comes from
/// fighting more/less, or from the same fight count paying off
/// differently (see the "PackLeader/Ambusher progress well but earn
/// less" finding this was built to chase down).
/// </summary>
public sealed record NpcOutcome(CreatureTraitKind Trait, int Level, int Credits, int InventoryCount, int FurthestYearReached, bool OwnsStore, int KillCount);

/// <summary>Result of <see cref="PlaytestRunner.RunSimultaneous"/> — the per-class reports plus one shared-world report holding NPC store activity and NPC outcomes (one world, one dataset).</summary>
public sealed class SimultaneousResult
{
    public required List<RunReport> PerClass { get; init; }
    public required RunReport World { get; init; }
    public int TotalTicks { get; set; }
}

/// <summary>Everything the harness recorded for one bot playthrough of one class.</summary>
public sealed class RunReport
{
    public required string CharacterName { get; init; }
    public required long WorldSeed { get; init; }

    public int FinalLevel { get; set; }
    public int FinalYear { get; set; }
    public int FurthestYearReached { get; set; }
    public bool DiedDuringRun { get; set; }
    public int TicksRun { get; set; }
    public int TicksSurvived { get; set; }

    public int Kills { get; set; }
    public int TotalXp { get; set; }
    public int MaxHitTaken { get; private set; }
    public int AmbushesObserved { get; set; }

    /// <summary>Shots actually fired via <see cref="ChronoTravelers.Engine.Combat.RangedResolver.Fire"/> — <see cref="RangedKills"/> is a subset (already folded into <see cref="Kills"/> too, for the true melee+ranged total).</summary>
    public int RangedShotsFired { get; set; }
    public int RangedShotsHit { get; set; }
    public int RangedKills { get; set; }

    /// <summary>
    /// Updates <see cref="MaxHitTaken"/> if <paramref name="damage"/> is a
    /// new high — callers report the damage from one concrete blow (one
    /// combat round, one ambush), never a coarser span like "before this
    /// fight vs. after it," which would silently sum every round's damage
    /// into what reads as a single spike (a real bug this once caused:
    /// seven ordinary 4-6 damage hits across a whole fight reported as one
    /// 35-damage hit).
    /// </summary>
    public void RecordHit(int damage)
    {
        if (damage > MaxHitTaken)
        {
            MaxHitTaken = damage;
        }
    }

    public int FinalCredits { get; set; }
    public int FinalTachyons { get; set; }

    /// <summary>Store repairs the bot performed this run (docs/GDD.md §6.3's durability/repair loop — <c>Store.Repair</c>), and the Credits spent on them. The bot repairs its equipped Weapon/Armor whenever it's at a store, worn, and can afford it — see <c>PlaytestRunner.TryShop</c>.</summary>
    public int RepairsPerformed { get; set; }
    public int CreditsSpentOnRepair { get; set; }

    /// <summary>True if the bot's equipped Weapon or Armor ever hit 0 Durability (<c>Item.IsBroken</c>) before it could get to a store — a sign the repair cadence isn't keeping up with wear (or the bot spent too long away from a store).</summary>
    public bool EquippedGearBrokeAtLeastOnce { get; set; }

    /// <summary>Weapon/armor/ranged wielded at run's end (on death, that's exactly what the bot went down holding) — null for an empty slot. See <c>PlaytestRunner.DescribeItem</c>.</summary>
    public string? EquippedWeaponAtEnd { get; set; }
    public string? EquippedArmorAtEnd { get; set; }
    public string? EquippedRangedAtEnd { get; set; }

    public Dictionary<string, AbilityUsage> AbilityUsage { get; } = [];
    public Dictionary<PassiveHook, PassiveUsage> PassiveUsage { get; } = [];
    public Dictionary<ConsumableEffectType, ConsumableUsage> ConsumableUsage { get; } = [];

    public void RecordConsumableUse(ConsumableEffectType effect, bool inCombat)
    {
        var usage = ConsumableUsage.TryGetValue(effect, out var u) ? u : ConsumableUsage[effect] = new ConsumableUsage();
        if (inCombat)
        {
            usage.InCombat++;
        }
        else
        {
            usage.OutOfCombat++;
        }
    }

    /// <summary>Passives this character had unlocked by <see cref="FinalLevel"/> that never got a recorded activation this run (either genuinely never triggered, or one of the "continuous, not observed" hooks — see ReportPrinter).</summary>
    public List<string> UnlockedButUnobserved { get; } = [];

    /// <summary>How many fights (regardless of outcome) the bot had against a monster with each <see cref="CreatureTraitKind"/> — recorded by FightBot.Fight. <c>None</c> is the ~60% trait-free baseline every other kind is compared against.</summary>
    public Dictionary<CreatureTraitKind, int> MonsterTraitsFought { get; } = [];

    /// <summary>The trait each NPC in the harness's spawned population carries, sampled at the end of the run (a respawned NPC rerolls, so this reflects current composition, not full-run history).</summary>
    public Dictionary<CreatureTraitKind, int> NpcTraitsObserved { get; } = [];

    /// <summary>Each spawned NPC's final state, for measuring a trait's actual effect (Credits, Level, inventory, store ownership) rather than just its spawn rate.</summary>
    public List<NpcOutcome> NpcOutcomes { get; } = [];

    /// <summary>Count of every <see cref="NpcGoal"/> an NPC resolved this run (via WorldSimulation.OnNpcAct) — the raw activity mix behind the store numbers below.</summary>
    public Dictionary<NpcGoal, int> NpcActionCounts { get; } = [];

    /// <summary>NPC <see cref="NpcGoal.Trade"/> actions (buy/sell) that landed at ANOTHER traveller's store ("&lt;name&gt;'s Store") rather than the year's government Depot — the direct measure of NPC-to-NPC store commerce.</summary>
    public int NpcTradesAtPlayerOrNpcStore { get; set; }

    /// <summary>NPC <see cref="NpcGoal.Trade"/> actions that landed at the year's government Depot instead.</summary>
    public int NpcTradesAtGovernmentStore { get; set; }

    /// <summary>Times an NPC bought a vacant store slot this run (<see cref="NpcGoal.OwnStore"/>, "bought ...").</summary>
    public int NpcStoreSlotPurchases { get; set; }

    /// <summary>Times an NPC tended a store it owns (paid maintenance / deposited capital / stocked / marked down / collected) — <see cref="NpcGoal.OwnStore"/>, everything that isn't a slot purchase.</summary>
    public int NpcOwnStoreTendActions { get; set; }

    /// <summary>A capped sample of the actual store-activity detail strings, for eyeballing what's happening.</summary>
    public List<string> NpcStoreActivitySamples { get; } = [];
}
