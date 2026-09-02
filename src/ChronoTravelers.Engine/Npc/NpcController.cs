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

    /// <summary>How often, per tick, an NPC that doesn't already own a store even considers buying an empty slot it can afford — kept low so NPCs don't snap up every vacancy the moment it appears. See <see cref="TryPurchaseStoreSlot"/>.</summary>
    private const double StorePurchaseChance = 0.05;

    /// <summary>How often, per tick, an NPC that already owns a store here tends it (pays maintenance, stocks surplus gear, or collects Capital) — docs/GDD.md §7's "occasionally visit/stock a store it owns."</summary>
    private const double StoreTendChance = 0.5;

    /// <summary>Tachyons an owner pays into its store's maintenance reserve in one tending action.</summary>
    private const int MaintenanceTopUpTachyons = 30;

    /// <summary>An owner tops up maintenance once the store's reserve dips below this many Tachyons.</summary>
    private const int MaintenanceReserveTarget = 60;

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
    /// safe to call anyway. <paramref name="storeSlots"/> may be omitted or
    /// empty, in which case the NPC never trades or buys a store (falls
    /// straight through to grinding) — pass every store slot in the NPC's
    /// current year (occupied AND vacant) so it can both shop/tend an
    /// owned store and consider buying an empty one (see
    /// <see cref="TryStoreVisit"/>). <paramref name="monsterRoster"/>
    /// defaults to the small sandbox Monsters.TestMonsters.All if omitted,
    /// for callers (existing tests) that don't care about real content;
    /// production callers should always pass the NPC's own level's real
    /// roster. <paramref name="world"/> is only needed for time-travel
    /// attempts — omit it (or leave null) to skip that behavior entirely.
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
        IReadOnlyList<StoreSlot>? storeSlots = null,
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

        if (storeSlots is { Count: > 0 })
        {
            var storeResult = TryStoreVisit(npc, storeSlots, random);
            if (storeResult is not null)
            {
                return storeResult;
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
    /// One store-related action per tick, in priority order: tend a store
    /// this NPC already owns here (<see cref="TryTendOwnStore"/>), else
    /// consider buying an empty slot (<see cref="TryPurchaseStoreSlot"/>),
    /// else fall back to ordinary shopping at whatever's occupied
    /// (<see cref="TryTrade"/>) — docs/GDD.md §7's "path to a store to
    /// trade ... occasionally visit/stock a store it owns."
    /// </summary>
    private static NpcTickResult? TryStoreVisit(Traveler npc, IReadOnlyList<StoreSlot> storeSlots, IRandomSource random)
    {
        var ownedSlot = storeSlots.FirstOrDefault(s => s.Store?.Owner == npc);
        if (ownedSlot is not null)
        {
            var tendResult = TryTendOwnStore(npc, ownedSlot, random);
            if (tendResult is not null)
            {
                return tendResult;
            }
        }
        else
        {
            var purchaseResult = TryPurchaseStoreSlot(npc, storeSlots, random);
            if (purchaseResult is not null)
            {
                return purchaseResult;
            }
        }

        var occupiedStores = storeSlots.Where(s => s.Store is not null).Select(s => s.Store!).ToList();
        return occupiedStores.Count > 0 ? TryTrade(npc, occupiedStores, random) : null;
    }

    /// <summary>
    /// Occasionally buys an empty store slot — docs/GDD.md §6.2/§7's "NPCs
    /// buy and tend stores." Never takes the LAST vacant slot in a year:
    /// at least one purchasable slot always stays open for the human
    /// player (on top of the always-open government store), so this only
    /// fires when 2 or more vacant slots remain here.
    /// </summary>
    private static NpcTickResult? TryPurchaseStoreSlot(Traveler npc, IReadOnlyList<StoreSlot> storeSlots, IRandomSource random)
    {
        var vacant = storeSlots.Where(s => s.IsAvailableForPurchase).ToList();
        if (vacant.Count < 2 || random.NextDouble() >= StorePurchaseChance)
        {
            return null;
        }

        var slot = vacant[(int)(random.NextDouble() * vacant.Count)];
        if (npc.Credits < slot.PurchaseCost)
        {
            return null;
        }

        slot.Purchase(npc);
        return new NpcTickResult(npc.Name, NpcGoal.OwnStore, Detail: $"bought {slot.Name}");
    }

    /// <summary>
    /// An NPC that already owns a store here occasionally pays down its
    /// Tachyon maintenance (<see cref="Store.ApplyMaintenanceTick"/> is
    /// what actually draws it down each world tick — this just tops the
    /// reserve up so foreclosure doesn't creep closer), lists a piece of
    /// surplus gear or junk for sale, or collects accumulated Capital into
    /// Credits — docs/GDD.md §7's "occasionally visit/stock a store it
    /// owns." At most one of the three per tick, same "one action" rule as
    /// everywhere else; maintenance is checked first since an unpaid store
    /// risks repossession.
    /// </summary>
    private static NpcTickResult? TryTendOwnStore(Traveler npc, StoreSlot ownedSlot, IRandomSource random)
    {
        if (random.NextDouble() >= StoreTendChance)
        {
            return null;
        }

        var store = ownedSlot.Store!;

        if (store.TachyonReserve < MaintenanceReserveTarget && npc.Tachyons.CanAfford(MaintenanceTopUpTachyons))
        {
            store.Charge(npc, MaintenanceTopUpTachyons);
            return new NpcTickResult(npc.Name, NpcGoal.OwnStore, Detail: $"paid maintenance at {store.Name}");
        }

        var surplus = npc.Inventory.FirstOrDefault(i => i.IsWieldable && !i.IsTimeShard && !IsEquipped(npc, i))
            ?? npc.Inventory.FirstOrDefault(i => i.Type == ItemType.Junk);
        if (surplus is not null)
        {
            var price = EconomyPricing.DefaultAskingPrice(surplus);
            store.Deposit(npc, surplus, price);
            return new NpcTickResult(npc.Name, NpcGoal.OwnStore, Detail: $"stocked {surplus.Name} at {store.Name} for {price} Credits");
        }

        if (store.Capital > 0)
        {
            var collected = store.CollectCapital(npc, store.Capital);
            return new NpcTickResult(npc.Name, NpcGoal.OwnStore, Detail: $"collected {collected} Credits from {store.Name}");
        }

        return null;
    }

    /// <summary>
    /// Sells one piece of surplus gear (a looted Weapon/Armor/Ranged item
    /// that isn't equipped — <see cref="Act"/> already claimed anything
    /// that would've been an upgrade, so what's left is genuine dead
    /// weight) or excess junk, or buys the cheapest affordable weapon if
    /// still unarmed — at most one action, at a randomly picked store.
    /// Selling gear is tried first: it's what actually makes a store worth
    /// browsing, junk is just Credit filler. Null if there was nothing
    /// worth doing (falls through to grinding). Called by
    /// <see cref="TryStoreVisit"/> with every occupied store here — this
    /// itself has no notion of ownership/vacancy.
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
