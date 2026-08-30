using Mutants.Core.Characters;
using Mutants.Core.Economy;
using Mutants.Core.Ions;
using Mutants.Core.Items;
using Mutants.Core.Levels;
using Mutants.Core.Monsters;
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

    /// <summary>
    /// How often, per tick, a ready NPC (not low on Ions/HP) even
    /// considers pushing one level deeper — keeps a level's population
    /// from draining out the moment everyone qualifies, since "considers"
    /// still requires affording the Ion cost and (for a fresh level)
    /// winning a real gatekeeper fight.
    /// </summary>
    private const double TravelAttemptChance = 0.10;

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
        GameWorld? world = null)
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
    /// Rolls whether to even attempt pushing one level deeper this tick,
    /// then only actually calls <see cref="TimeTravelResolver.Travel"/> —
    /// spending Ions and possibly fighting a gatekeeper — once the level
    /// exists and both the character-level and Ion-cost gates are already
    /// known to pass, so an NPC never "wastes" a tick failing a check it
    /// could see coming; only a genuine gatekeeper loss returns as a
    /// failed <see cref="NpcGoal.Travel"/> result here. Null means nothing
    /// happened - the caller falls through to trade/grind as normal.
    /// NPCs only ever push forward (never retreat) - there's no goal that
    /// would make an autonomous NPC want to go shallower once established
    /// somewhere.
    /// </summary>
    private static NpcTickResult? TryTravel(Mutant npc, GameWorld world, IRandomSource random)
    {
        if (random.NextDouble() > TravelAttemptChance)
        {
            return null;
        }

        var targetLevel = npc.CurrentTimeLevel + 1;
        var targetDefinition = world.TryGetLevel(targetLevel);
        if (targetDefinition is null)
        {
            return null; // already at the deepest level that exists
        }

        if (npc.Level < targetDefinition.MinCharacterLevelToUnlock)
        {
            return null; // not strong enough yet
        }

        if (!npc.Ions.CanAfford(IonEconomy.TimeTravelCost(targetLevel)))
        {
            return null; // can't afford the jump yet
        }

        var result = TimeTravelResolver.Travel(npc, world, targetLevel, random);

        // Only set MonsterName (which WorldSimulation.Tick uses to broadcast a
        // win/loss Slain event, same as a normal grind-fight) when a gatekeeper
        // was actually fought - a plain Ion-cost hop to an already-unlocked
        // level has no fight to report here (its own TimeTraveled broadcast
        // covers that case instead).
        var gatekeeperName = result.GatekeeperFight is not null ? $"The Gatekeeper of Level {targetLevel}" : null;

        return result.Success
            ? new NpcTickResult(npc.Name, NpcGoal.Travel, gatekeeperName, result.GatekeeperFight, $"time traveled to level {targetLevel}")
            : new NpcTickResult(npc.Name, NpcGoal.Travel, gatekeeperName, result.GatekeeperFight, $"was defeated by level {targetLevel}'s gatekeeper");
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
