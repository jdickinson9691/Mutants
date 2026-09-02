using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Economy;
using ChronoTravelers.Core.Tachyons;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Monsters;
using ChronoTravelers.Core.Time;
using ChronoTravelers.Core.World;
using ChronoTravelers.Engine.Combat;
using ChronoTravelers.Engine.Simulation;

namespace ChronoTravelers.Engine.Npc;

/// <summary>
/// The per-tick NPC behavior loop — docs/GDD.md §7: "assess Tachyon level
/// (seek conversion fodder or a store if low), assess HP (retreat/heal if
/// low), otherwise pursue its current goal (grind monsters near its
/// level, path to a store to trade, attempt a time-travel jump if it has
/// both the Tachyons and the level-unlock, occasionally visit/stock a store
/// it owns)." There's no spatial store placement/pathing here either:
/// like ChronoTravelers.Console's "fight" command, an NPC's store visit picks a
/// random known store rather than actually traveling to one — a
/// deliberate simplification consistent with the GDD's own "not full
/// pathfinding AI/ML" note. Decision-making is intentionally simple and
/// rule-based; the specific Tachyon/HP/inventory/travel thresholds below are
/// original tuning, not GDD-specified.
/// </summary>
public static class NpcController
{
    private const double LowTachyonThreshold = 0.25;
    private const double LowHealthThreshold = 0.30;
    private const int ExcessJunkThreshold = 3;

    /// <summary>How often, per tick, a ready NPC not pulling toward an anchor (not low on Tachyons/HP, either background-pool or already at the anchor) even considers jumping to another year — kept low so a year's population doesn't churn every tick.</summary>
    private const double TravelAttemptChance = 0.10;

    /// <summary>How often, per tick, a local-pool NPC (see <see cref="NpcPopulation.LocalPopulationTarget"/>) that ISN'T at the anchor year even considers closing the gap — much higher than <see cref="TravelAttemptChance"/> so "5 active near the player" actually converges within a handful of ticks instead of drifting for a real-time-equivalent age.</summary>
    private const double AnchorTravelAttemptChance = 0.6;

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
    /// <paramref name="anchorYear"/> is the year to gravitate toward — the
    /// player's current year in single-player, or a rotating occupied year
    /// on the shared-world server; <paramref name="pullToAnchor"/> is true
    /// only for the population's "local pool" (see
    /// <see cref="NpcPopulation.LocalPopulationTarget"/>), so a background
    /// NPC keeps the original low-chance wander-anywhere travel instead.
    /// </summary>
    public static NpcTickResult Act(
        Traveler npc,
        LevelMap level,
        IRandomSource random,
        IReadOnlyList<Store>? stores = null,
        IReadOnlyList<Func<Monster>>? monsterRoster = null,
        TimeWorld? world = null,
        int? anchorYear = null,
        bool pullToAnchor = false)
    {
        if (npc.Health.IsDead)
        {
            return new NpcTickResult(npc.Name, NpcGoal.Idle);
        }

        if (IsLow(npc.Tachyons.Current, npc.Tachyons.Max, LowTachyonThreshold))
        {
            var convertible = npc.Inventory.FirstOrDefault(i => i.Type == ItemType.Junk)
                               ?? npc.Inventory.FirstOrDefault();
            if (convertible is not null)
            {
                npc.Convert(convertible);
                return new NpcTickResult(npc.Name, NpcGoal.SeekTachyons);
            }
            // No fodder on hand - fall through to grinding, which is also how they'll find more.
        }

        if (IsLow(npc.Health.Current, npc.Health.Max, LowHealthThreshold))
        {
            return new NpcTickResult(npc.Name, NpcGoal.Retreat);
        }

        if (world is not null)
        {
            var travelResult = TryTravel(npc, world, random, anchorYear, pullToAnchor);
            if (travelResult is not null)
            {
                return travelResult;
            }
        }

        // A better weapon/armor/ranged item already sitting in the pack
        // from a kill (not bought) gets worn immediately — a player would
        // never carry a stronger sword around unequipped, so neither
        // should an NPC. This also means "sell one" below only ever sees
        // genuine surplus, never something worth wielding.
        var upgrade = FindUpgrade(npc);
        if (upgrade is not null)
        {
            npc.Wield(upgrade);
            return new NpcTickResult(npc.Name, NpcGoal.Upgrade, Detail: $"wielded {upgrade.Name}");
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
    /// Rolls whether to jump to another year this tick. A local-pool NPC
    /// (<paramref name="pullToAnchor"/>) not already at
    /// <paramref name="anchorYear"/> rolls against
    /// <see cref="AnchorTravelAttemptChance"/> and heads straight for it —
    /// the full jump if it can afford the Tachyon cost, otherwise the
    /// biggest hop toward it it can afford (overshoot clamped to the
    /// anchor), so distance keeps closing across ticks even when the full
    /// jump is out of reach. Everyone else (background-pool NPCs, or a
    /// local-pool NPC that's already there) keeps the original low-chance,
    /// mostly-forward random hop — see <see cref="ForwardTravelBias"/>.
    /// Null means nothing happened — the caller falls through to
    /// upgrade/trade/grind. There is no Warden fight during travel any
    /// more, so this never "loses"; a failed result just means the jump
    /// was unaffordable and is reported as such.
    /// </summary>
    private const double ForwardTravelBias = 0.8;

    private static NpcTickResult? TryTravel(Traveler npc, TimeWorld world, IRandomSource random, int? anchorYear, bool pullToAnchor)
    {
        int targetYear;

        if (pullToAnchor && anchorYear is { } anchor && anchor != npc.CurrentYear)
        {
            if (random.NextDouble() > AnchorTravelAttemptChance)
            {
                return null;
            }

            targetYear = npc.Tachyons.CanAfford(TachyonEconomy.TimeTravelCost(npc.CurrentYear, anchor))
                ? anchor
                : ClosestAffordableHopToward(npc, anchor, random);
        }
        else
        {
            if (random.NextDouble() > TravelAttemptChance)
            {
                return null;
            }

            var hop = MinTravelHop + (int)(random.NextDouble() * (MaxTravelHop - MinTravelHop + 1));
            var forward = random.NextDouble() < ForwardTravelBias;
            targetYear = Math.Clamp(npc.CurrentYear + (forward ? hop : -hop), TimeScale.MinYear, TimeScale.MaxYear);
        }

        if (targetYear == npc.CurrentYear)
        {
            return null;
        }

        if (!npc.Tachyons.CanAfford(TachyonEconomy.TimeTravelCost(npc.CurrentYear, targetYear)))
        {
            return null;
        }

        var result = TimeTravelResolver.Travel(npc, world, targetYear, random);
        return result.Success
            ? new NpcTickResult(npc.Name, NpcGoal.Travel, null, null, $"time traveled to {targetYear} A.D.")
            : null;
    }

    /// <summary>A hop of up to <see cref="MaxTravelHop"/> years toward <paramref name="anchor"/>, never overshooting past it — used when the full jump to the anchor isn't affordable yet, so a local-pool NPC still makes real progress this tick instead of doing nothing.</summary>
    private static int ClosestAffordableHopToward(Traveler npc, int anchor, IRandomSource random)
    {
        var direction = anchor > npc.CurrentYear ? 1 : -1;
        var hop = MinTravelHop + (int)(random.NextDouble() * (MaxTravelHop - MinTravelHop + 1));
        var target = Math.Clamp(npc.CurrentYear + direction * hop, TimeScale.MinYear, TimeScale.MaxYear);
        return direction > 0 ? Math.Min(target, anchor) : Math.Max(target, anchor);
    }

    private static bool IsLow(int current, int max, double threshold) => max > 0 && current < max * threshold;

    /// <summary>True if <paramref name="item"/> is the item currently occupying its slot (Weapon/Armor/Ranged) — an item can be both "in Inventory" and equipped at once, per Traveler.Wield, so selling/upgrade logic has to explicitly exclude whichever's worn.</summary>
    private static bool IsEquipped(Traveler npc, Item item) =>
        item == npc.EquippedWeapon || item == npc.EquippedArmor || item == npc.EquippedRanged;

    /// <summary>
    /// The best currently-unequipped Weapon/Armor/Ranged item in the pack
    /// that beats what's in that slot right now, by the same
    /// WieldEffectiveness-scaled bonus combat actually uses (so an
    /// off-class find has to clear a higher bar) — at most one per tick,
    /// same "one action" rule as everything else here. A depleted ranged
    /// weapon never counts; an empty slot counts as a current value of 0,
    /// so anything wieldable and not-worse always wins an empty slot.
    /// </summary>
    private static Item? FindUpgrade(Traveler npc)
    {
        Item? best = null;
        var bestGain = 0.0;

        foreach (var item in npc.Inventory)
        {
            if (!item.IsWieldable || IsEquipped(npc, item) || (item.IsRanged && item.IsDepleted))
            {
                continue;
            }

            var current = item.Type switch
            {
                ItemType.Weapon => npc.EquippedWeapon is { } w ? w.AttackBonus * w.WieldEffectiveness(npc.Class) : 0,
                ItemType.Armor => npc.EquippedArmor is { } a ? a.DefenseBonus * a.WieldEffectiveness(npc.Class) : 0,
                ItemType.Ranged => npc.EquippedRanged is { } r ? r.AttackBonus * r.WieldEffectiveness(npc.Class) : 0,
                _ => double.MaxValue, // not a slot we manage here - never "an upgrade"
            };
            var candidate = item.Type switch
            {
                ItemType.Weapon or ItemType.Ranged => item.AttackBonus * item.WieldEffectiveness(npc.Class),
                ItemType.Armor => item.DefenseBonus * item.WieldEffectiveness(npc.Class),
                _ => 0.0,
            };

            var gain = candidate - current;
            if (gain > bestGain)
            {
                bestGain = gain;
                best = item;
            }
        }

        return best;
    }

    /// <summary>
    /// Sells one piece of surplus gear (a looted Weapon/Armor/Ranged item
    /// that isn't equipped — <see cref="Act"/> already claimed anything
    /// that would've been an upgrade, so what's left is genuine dead
    /// weight) or excess junk, or buys the cheapest affordable weapon if
    /// still unarmed — at most one action, at a randomly picked store.
    /// Selling gear is tried first: it's what actually makes a store worth
    /// browsing, junk is just Credit filler. Null if there was nothing
    /// worth doing (falls through to grinding).
    /// </summary>
    private static NpcTickResult? TryTrade(Traveler npc, IReadOnlyList<Store> stores, IRandomSource random)
    {
        var surplusGear = npc.Inventory.FirstOrDefault(i => i.IsWieldable && !i.IsTimeShard && !IsEquipped(npc, i));
        var junkCount = npc.Inventory.Count(i => i.Type == ItemType.Junk);
        var wantsToSellGear = surplusGear is not null;
        var wantsToSellJunk = junkCount > ExcessJunkThreshold;
        var wantsToBuyWeapon = npc.EquippedWeapon is null && npc.Credits > 0;

        if (!wantsToSellGear && !wantsToSellJunk && !wantsToBuyWeapon)
        {
            return null;
        }

        var store = stores[(int)(random.NextDouble() * stores.Count)];

        if (wantsToSellGear)
        {
            var price = store.BuyFromTraveler(npc, surplusGear!);
            if (price is not null)
            {
                return new NpcTickResult(npc.Name, NpcGoal.Trade, Detail: $"sold {surplusGear!.Name} to {store.Name} for {price} Credits");
            }
        }

        if (wantsToSellJunk)
        {
            var junk = npc.Inventory.First(i => i.Type == ItemType.Junk);
            var price = store.BuyFromTraveler(npc, junk);
            if (price is not null)
            {
                return new NpcTickResult(npc.Name, NpcGoal.Trade, Detail: $"sold {junk.Name} to {store.Name} for {price} Credits");
            }
        }

        if (wantsToBuyWeapon)
        {
            var affordable = store.Listings
                .Where(l => l.Item.Type == ItemType.Weapon && l.AskingPrice <= npc.Credits)
                .OrderBy(l => l.AskingPrice)
                .FirstOrDefault();

            if (affordable is not null)
            {
                store.SellToTraveler(npc, affordable);
                npc.Wield(affordable.Item);
                return new NpcTickResult(npc.Name, NpcGoal.Trade, Detail: $"bought and wielded {affordable.Item.Name} from {store.Name}");
            }
        }

        return null; // wanted to trade but nothing worked out this tick
    }

    private static void Wander(Traveler npc, LevelMap level, IRandomSource random)
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
