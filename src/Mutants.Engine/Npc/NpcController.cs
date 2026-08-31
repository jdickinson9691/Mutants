using Mutants.Core.Characters;
using Mutants.Core.Economy;
using Mutants.Core.Ions;
using Mutants.Core.Items;
using Mutants.Core.Monsters;
using Mutants.Core.Time;
using Mutants.Core.World;
using Mutants.Engine.Combat;
using Mutants.Engine.Simulation;

namespace Mutants.Engine.Npc;

/// <summary>
/// The per-tick NPC behavior loop — docs/GDD.md §7: "assess Ion level
/// (seek conversion fodder or a store if low), assess HP (retreat/heal if
/// low), otherwise pursue its current goal (grind monsters near its
/// level, path to a store to trade, attempt a time-travel jump if it has
/// both the Ions and the level-unlock, occasionally visit/stock a store
/// it owns)." There's no spatial store placement/pathing here either:
/// like Mutants.Console's "fight" command, an NPC's store visit picks a
/// random known store rather than actually traveling to one — a
/// deliberate simplification consistent with the GDD's own "not full
/// pathfinding AI/ML" note. Decision-making is intentionally simple and
/// rule-based; the specific Ion/HP/inventory/travel thresholds below are
/// original tuning, not GDD-specified.
/// </summary>
public static class NpcController
{
    private const double LowIonThreshold = 0.25;
    private const double LowHealthThreshold = 0.30;
    private const int ExcessJunkThreshold = 3;

    /// <summary>How often, per tick, a ready NPC (not low on Ions/HP) even considers jumping to another year — kept low so a year's population doesn't churn every tick.</summary>
    private const double TravelAttemptChance = 0.10;

    /// <summary>An NPC's per-jump reach, in years — it picks a random offset in ±[<see cref="MinTravelHop"/>, <see cref="MaxTravelHop"/>], clamped to the timeline.</summary>
    private const int MinTravelHop = 50;
    private const int MaxTravelHop = 300;

    /// <summary>
    /// Decides and executes one tick's action for <paramref name="npc"/>.
    /// A defeated NPC does nothing (<see cref="NpcGoal.Idle"/>) — callers
    /// should generally skip calling this for dead NPCs entirely, but it's
    /// safe to call anyway. <paramref name="stores"/> may be omitted or
    /// empty, in which case the NPC never trades (falls straight through
    /// to grinding). <paramref name="monsterRoster"/> defaults to the
    /// small sandbox Monsters.TestMonsters.All if omitted, for callers
    /// (existing tests) that don't care about real content; production
    /// callers should always pass the NPC's own level's real roster.
    /// <paramref name="world"/> is only needed for time-travel attempts —
    /// omit it (or leave null) to skip that behavior entirely.
    /// </summary>
    public static NpcTickResult Act(
        Mutant npc,
        LevelMap level,
        IRandomSource random,
        IReadOnlyList<Store>? stores = null,
        IReadOnlyList<Func<Monster>>? monsterRoster = null,
        TimeWorld? world = null)
    {
        if (npc.Health.IsDead)
        {
            return new NpcTickResult(npc.Name, NpcGoal.Idle);
        }

        if (IsLow(npc.Ions.Current, npc.Ions.Max, LowIonThreshold))
        {
            var convertible = npc.Inventory.FirstOrDefault(i => i.Type == ItemType.Junk)
                               ?? npc.Inventory.FirstOrDefault();
            if (convertible is not null)
            {
                npc.Convert(convertible);
                return new NpcTickResult(npc.Name, NpcGoal.SeekIons);
            }
            // No fodder on hand - fall through to grinding, which is also how they'll find more.
        }

        if (IsLow(npc.Health.Current, npc.Health.Max, LowHealthThreshold))
        {
            return new NpcTickResult(npc.Name, NpcGoal.Retreat);
        }

        if (world is not null)
        {
            var travelResult = TryTravel(npc, world, random);
            if (travelResult is not null)
            {
                return travelResult;
            }
        }

        if (stores is { Count: > 0 })
        {
            var tradeResult = TryTrade(npc, stores, random);
            if (tradeResult is not null)
            {
                return tradeResult;
            }
        }

        Wander(npc, level, random);

        var roster = monsterRoster ?? TestMonsters.All;
        var monster = roster[(int)(random.NextDouble() * roster.Count)]();
        var fight = CombatResolver.Fight(npc, monster, random);

        return new NpcTickResult(npc.Name, NpcGoal.Grind, monster.Name, fight);
    }

    /// <summary>
    /// Rolls whether to jump to another year this tick, picks a target a
    /// short hop away (usually forward — see <see cref="ForwardTravelBias"/>),
    /// and calls <see cref="TimeTravelResolver.Travel"/> if the NPC can
    /// afford the Ion cost. Null means nothing happened — the caller falls
    /// through to trade/grind. There is no Gatekeeper fight during travel
    /// any more, so this never "loses"; a failed result just means the
    /// jump was unaffordable and is reported as such.
    /// </summary>
    private const double ForwardTravelBias = 0.8;

    private static NpcTickResult? TryTravel(Mutant npc, TimeWorld world, IRandomSource random)
    {
        if (random.NextDouble() > TravelAttemptChance)
        {
            return null;
        }

        var hop = MinTravelHop + (int)(random.NextDouble() * (MaxTravelHop - MinTravelHop + 1));
        var forward = random.NextDouble() < ForwardTravelBias;
        var targetYear = Math.Clamp(
            npc.CurrentYear + (forward ? hop : -hop),
            TimeScale.MinYear,
            TimeScale.MaxYear);

        if (targetYear == npc.CurrentYear)
        {
            return null;
        }

        if (!npc.Ions.CanAfford(IonEconomy.TimeTravelCost(npc.CurrentYear, targetYear)))
        {
            return null;
        }

        var result = TimeTravelResolver.Travel(npc, world, targetYear, random);
        return result.Success
            ? new NpcTickResult(npc.Name, NpcGoal.Travel, null, null, $"time traveled to {targetYear} A.D.")
            : null;
    }

    private static bool IsLow(int current, int max, double threshold) => max > 0 && current < max * threshold;

    /// <summary>
    /// Sells one excess junk item, or buys the cheapest affordable weapon
    /// if unarmed — at most one action, at a randomly picked store. Null
    /// if there was nothing worth doing (falls through to grinding).
    /// </summary>
    private static NpcTickResult? TryTrade(Mutant npc, IReadOnlyList<Store> stores, IRandomSource random)
    {
        var junkCount = npc.Inventory.Count(i => i.Type == ItemType.Junk);
        var wantsToSell = junkCount > ExcessJunkThreshold;
        var wantsToBuyWeapon = npc.EquippedWeapon is null && npc.Riblets > 0;

        if (!wantsToSell && !wantsToBuyWeapon)
        {
            return null;
        }

        var store = stores[(int)(random.NextDouble() * stores.Count)];

        if (wantsToSell)
        {
            var junk = npc.Inventory.First(i => i.Type == ItemType.Junk);
            var price = store.BuyFromMutant(npc, junk);
            if (price is not null)
            {
                return new NpcTickResult(npc.Name, NpcGoal.Trade, Detail: $"sold {junk.Name} to {store.Name} for {price} Riblets");
            }
        }

        if (wantsToBuyWeapon)
        {
            var affordable = store.Listings
                .Where(l => l.Item.Type == ItemType.Weapon && l.AskingPrice <= npc.Riblets)
                .OrderBy(l => l.AskingPrice)
                .FirstOrDefault();

            if (affordable is not null)
            {
                store.SellToMutant(npc, affordable);
                npc.Wield(affordable.Item);
                return new NpcTickResult(npc.Name, NpcGoal.Trade, Detail: $"bought and wielded {affordable.Item.Name} from {store.Name}");
            }
        }

        return null; // wanted to trade but nothing worked out this tick
    }

    private static void Wander(Mutant npc, LevelMap level, IRandomSource random)
    {
        var room = level.GetRoom(npc.Position);
        var exits = room.ExitDescriptions.Keys.ToList();
        if (exits.Count == 0)
        {
            return;
        }

        var direction = exits[(int)(random.NextDouble() * exits.Count)];
        var move = level.TryMove(npc.Position, direction);
        if (move.Success)
        {
            npc.MoveTo(move.Destination!.Value);
        }
    }
}
