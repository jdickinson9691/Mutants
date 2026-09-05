using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Economy;
using ChronoTravelers.Core.Tachyons;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Monsters;
using ChronoTravelers.Core.Time;
using ChronoTravelers.Core.Traits;
using ChronoTravelers.Core.World;
using ChronoTravelers.Engine.Combat;
using ChronoTravelers.Engine.Content;
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

    /// <summary>How often, per tick, an NPC with Credits at an occupied store actually buys something from it (a listed upgrade, else sometimes a consumable). Gated so listings drain gradually rather than being hoovered the moment an NPC has cash — without any buying at all, owners and sellers only ever ADD listings and the shelf count climbs forever.</summary>
    private const double ShopPurchaseChance = 0.3;

    /// <summary>An owner stops auto-stocking its own surplus once its shopfront holds this many listings — the demand side (player + NPC buyers) can't keep up with an NPC that grinds nonstop, so without a ceiling the shelf just climbs to <see cref="Store.MaxListings"/>. A player stocking by hand is unaffected.</summary>
    private const int StoreStockSoftCap = 10;

    /// <summary>How often, per tick, an owner tending a shopfront that's OVER <see cref="StoreStockSoftCap"/> marks down its oldest unsold listing (<see cref="Store.ClearOldestListing"/>) — a buyer-independent drain so an over-producing owner settles around the soft cap instead of pinning at the hard cap.</summary>
    private const double StoreClearanceChance = 0.35;

    /// <summary>Credits an owner pays into its store's maintenance reserve in one tending action. Originally Tachyons; switched to Credits alongside <see cref="Store.CreditReserve"/> (docs/GDD.md §6.2) so a store's upkeep no longer competes with its owner's own survival/travel/heal Tachyon budget.</summary>
    private const int MaintenanceTopUpCredits = 30;

    /// <summary>An owner tops up maintenance once the store's reserve dips below this many Credits.</summary>
    private const int MaintenanceReserveTarget = 60;

    /// <summary>Credits an owner deposits into its own store's Capital in one tending action — the funding half of the loop that lets the store later buy from travelers via <see cref="Store.BuyFromTraveler"/>. See <see cref="CapitalReserveTarget"/>.</summary>
    private const int CapitalTopUpAmount = 150;

    /// <summary>
    /// An owner tops up its store's Capital once it dips below this floor,
    /// and — the other half of the same fix — only ever collects profit
    /// ABOVE this floor rather than sweeping the whole balance. Before this
    /// existed, <see cref="TryTendOwnStore"/> would collect every last
    /// Credit of Capital the moment there was any to collect and never
    /// deposited anything back in, so an owned store's Capital sat at (or
    /// returned to) 0 almost immediately — meaning it could sell listings
    /// but could never actually buy anything from a traveler, since
    /// <see cref="Store.BuyFromTraveler"/> refuses when Capital can't cover
    /// the price. This is what actually reported as "NPCs aren't
    /// depositing Credits for the store to function with."
    /// </summary>
    private const int CapitalReserveTarget = 300;

    /// <summary>How often, per tick, a ready NPC not pulling toward an anchor (not low on Tachyons/HP, either background-pool or already at the anchor) even considers jumping to another year — kept low so a year's population doesn't churn every tick.</summary>
    private const double TravelAttemptChance = 0.10;

    /// <summary>How often, per tick, a local-pool NPC (see <see cref="NpcPopulation.LocalPopulationTarget"/>) that ISN'T at the anchor year even considers closing the gap — much higher than <see cref="TravelAttemptChance"/> so "5 active near the player" actually converges within a handful of ticks instead of drifting for a real-time-equivalent age.</summary>
    private const double AnchorTravelAttemptChance = 0.6;

    /// <summary>An NPC's per-jump reach, in years — it picks a random offset in ±[<see cref="MinTravelHop"/>, <see cref="MaxTravelHop"/>], clamped to the timeline.</summary>
    private const int MinTravelHop = 50;
    private const int MaxTravelHop = 300;

    // --- CreatureTraitKind hooks (original tuning) ------------------------

    /// <summary>Aggressive trait — only retreats far closer to death than the normal <see cref="LowHealthThreshold"/>.</summary>
    private const double AggressiveLowHealthThreshold = 0.12;

    /// <summary>Skittish trait — retreats far earlier than the normal <see cref="LowHealthThreshold"/>.</summary>
    private const double SkittishLowHealthThreshold = 0.5;

    /// <summary>Scavenger trait — bonus Credits kept on top of a sale, mirroring <see cref="Monsters.Monster"/>'s Scavenger Convert bonus.</summary>
    private const double ScavengerSellBonusPct = 0.25;

    /// <summary>Trader trait — multiplies <see cref="StorePurchaseChance"/> and <see cref="StoreTendChance"/>, so a Trader is far more likely to buy and tend a shopfront.</summary>
    private const double TraderStoreActivityMultiplier = 2.5;

    /// <summary>Trader trait — extra stocking headroom above <see cref="StoreStockSoftCap"/> per NPC level, so a Trader's shelf keeps growing as it levels up (per the user's original "continue to collect and stock their shops as they level up" request).</summary>
    private const double TraderStockCapPerLevel = 0.4;

    /// <summary>Wanderer trait — multiplies both <see cref="TravelAttemptChance"/> and <see cref="AnchorTravelAttemptChance"/>, so it considers a jump far more often.</summary>
    private const double WandererTravelAttemptMultiplier = 2.5;

    /// <summary>Wanderer trait — multiplies its per-jump reach (<see cref="MinTravelHop"/>/<see cref="MaxTravelHop"/>).</summary>
    private const double WandererHopMultiplier = 1.5;

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
    /// <paramref name="abilities"/> is the full class ability catalog
    /// (ChronoTravelers.Content's abilities.json via ContentLoader.LoadAbilities);
    /// when non-empty, a grind fight is played out round-by-round through
    /// <see cref="CombatSession"/> — the same engine the player's own
    /// interactive `fight` uses — with <see cref="ChooseAbility"/> standing
    /// in for the player's per-round choice, instead of the instant,
    /// attack-only <see cref="CombatResolver.Fight"/>. Omit it (or pass an
    /// empty list, the default) to keep the original ability-free grind —
    /// existing callers/tests that don't care about abilities are
    /// unaffected.
    /// </summary>
    public static NpcTickResult Act(
        Traveler npc,
        LevelMap level,
        IRandomSource random,
        IReadOnlyList<StoreSlot>? storeSlots = null,
        IReadOnlyList<Func<Monster>>? monsterRoster = null,
        TimeWorld? world = null,
        int? anchorYear = null,
        bool pullToAnchor = false,
        IReadOnlyList<AbilityData>? abilities = null)
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

        // Aggressive / Skittish traits — retreat much later or much earlier than the norm.
        var retreatThreshold = npc.Trait switch
        {
            CreatureTraitKind.Aggressive => AggressiveLowHealthThreshold,
            CreatureTraitKind.Skittish => SkittishLowHealthThreshold,
            _ => LowHealthThreshold,
        };
        if (IsLow(npc.Health.Current, npc.Health.Max, retreatThreshold))
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
        var fight = abilities is { Count: > 0 }
            ? FightWithAbilities(npc, monster, abilities, random)
            : CombatResolver.Fight(npc, monster, random);

        return new NpcTickResult(npc.Name, NpcGoal.Grind, monster.Name, fight);
    }

    /// <summary>
    /// How often, per round, an NPC that isn't in HP trouble (see
    /// <see cref="EmergencyHealHpFraction"/>) even considers casting an
    /// ability instead of a plain attack — rations Tachyons across a
    /// session's worth of fights rather than dumping the whole pool into
    /// the first monster it meets, the same way a careful player would.
    /// </summary>
    private const double AbilityCastChance = 0.6;

    /// <summary>Below this fraction of max HP, an NPC always tries to cast its best available Heal-effect ability first, skipping the <see cref="AbilityCastChance"/> roll entirely — self-preservation before optimization.</summary>
    private const double EmergencyHealHpFraction = 0.4;

    /// <summary>Safety valve mirroring <see cref="CombatResolver.Fight"/>'s own — see that constant's doc comment.</summary>
    private const int AbilityFightMaxRounds = 200;

    /// <summary>
    /// Plays one grind fight round-by-round through <see cref="CombatSession"/>
    /// — the same engine backing the player's own interactive `fight` — so
    /// an NPC's class abilities (unlocked by level, gated by Tachyons,
    /// exactly as <see cref="CombatSession.Cast"/> already enforces for a
    /// human) are actually reachable in NPC combat, not just inert level-up
    /// data. <see cref="ChooseAbility"/> picks the round's action; a cast
    /// that somehow fails (should not normally happen, since it's only ever
    /// offered an ability it already believes is usable/affordable) falls
    /// back to a plain attack rather than wasting the round. On a win, loot
    /// goes straight into the NPC's pack — same "abstract, off-grid" grind
    /// convention <see cref="CombatResolver.AwardVictory"/> documents for
    /// the non-interactive path, since this fight (like that one) is
    /// against a roster monster, not one actually standing in the NPC's
    /// room.
    /// </summary>
    private static FightResult FightWithAbilities(Traveler npc, Monster monster, IReadOnlyList<AbilityData> abilities, IRandomSource random)
    {
        var usableAbilities = abilities
            .Where(a => string.Equals(a.Class, npc.Class.ToString(), StringComparison.OrdinalIgnoreCase)
                && a.Level <= npc.Level
                && !string.Equals(a.Effect, "None", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var session = new CombatSession(npc, monster, random);
        var rounds = 0;

        while (!session.IsOver && rounds < AbilityFightMaxRounds)
        {
            rounds++;

            var chosen = usableAbilities.Count > 0 ? ChooseAbility(npc, monster, usableAbilities, random) : null;
            if (chosen is null || !session.Cast(chosen).Success)
            {
                session.Attack();
            }
        }

        if (session.TravelerWon)
        {
            foreach (var item in session.ItemsDropped)
            {
                npc.AddToInventory(item);
            }
        }

        return new FightResult(session.TravelerWon, session.Rounds, session.XpAwarded, session.CreditsAwarded, session.ItemsDropped, session.Log);
    }

    /// <summary>
    /// Picks this round's ability, or null to just attack. Below
    /// <see cref="EmergencyHealHpFraction"/> HP this always evaluates
    /// (self-preservation first); otherwise it rolls
    /// <see cref="AbilityCastChance"/> for whether to bother at all, then
    /// takes the highest-<see cref="ScoreAbility"/> option the NPC can
    /// currently afford. Returns null with nothing affordable/usable.
    /// </summary>
    private static AbilityData? ChooseAbility(Traveler npc, Monster monster, IReadOnlyList<AbilityData> usableAbilities, IRandomSource random)
    {
        var hpFraction = npc.Health.Max > 0 ? npc.Health.Current / (double)npc.Health.Max : 1.0;
        var isEmergency = hpFraction < EmergencyHealHpFraction;

        if (!isEmergency && random.NextDouble() >= AbilityCastChance)
        {
            return null;
        }

        AbilityData? best = null;
        var bestScore = 0.0;

        foreach (var ability in usableAbilities)
        {
            if (!npc.Tachyons.CanAfford(ability.TachyonCost))
            {
                continue;
            }

            var score = ScoreAbility(ability, npc, monster, hpFraction);
            if (score > bestScore)
            {
                bestScore = score;
                best = ability;
            }
        }

        return best;
    }

    /// <summary>
    /// A rough "how good is this cast right now" heuristic — not a
    /// GDD-specified formula, original tuning meant to approximate how a
    /// reasonable player would spend Tachyons: an outright win button
    /// (<see cref="AbilityEffectType.InstantDefeatNonBoss"/>) always wins,
    /// Heal scales with how hurt the NPC actually is, a Damage-family
    /// ability whose <c>Condition</c> is currently met gets a bonus for
    /// not being wasted, and Restore Tachyons only matters when the pool is
    /// actually low. Effect types this engine can't act on
    /// (<see cref="AbilityEffectType.None"/> or an unparseable string)
    /// score below the zero floor <see cref="ChooseAbility"/> requires, so
    /// they're never picked.
    /// </summary>
    private static double ScoreAbility(AbilityData ability, Traveler npc, Monster monster, double hpFraction)
    {
        if (!Enum.TryParse<AbilityEffectType>(ability.Effect, ignoreCase: true, out var effect))
        {
            return -1;
        }

        var conditionBonus = !string.IsNullOrEmpty(ability.Condition) && ConditionCurrentlyMet(ability.Condition, ability.Tag, monster) ? 5.0 : 0.0;

        return effect switch
        {
            AbilityEffectType.InstantDefeatNonBoss => 1000,
            AbilityEffectType.Heal => (1.0 - hpFraction) * 25,
            AbilityEffectType.DamageOverTime => 12 + ability.Magnitude + conditionBonus,
            AbilityEffectType.ExtraAttack => 11,
            AbilityEffectType.IgnoreDefenseDamage => 10 + ability.Magnitude + conditionBonus,
            AbilityEffectType.Damage => 9 + ability.Magnitude + conditionBonus,
            AbilityEffectType.GuaranteedCritNextAttack => 8,
            AbilityEffectType.DebuffTargetDefense => 7,
            AbilityEffectType.DebuffTargetAttack => 6,
            AbilityEffectType.DebuffTargetSpeed => 5,
            AbilityEffectType.BuffSelfAttack => 5,
            AbilityEffectType.BuffSelfDefense => 4,
            AbilityEffectType.Shield => 4,
            AbilityEffectType.RestoreTachyons => npc.Tachyons.Current < npc.Tachyons.Max * 0.3 ? 15 : 0,
            _ => -1,
        };
    }

    /// <summary>Mirrors <see cref="CombatSession"/>'s own private condition check (see its <c>PerformTravelerAttack</c>) so scoring can tell whether a conditional Damage ability's bonus would actually land right now.</summary>
    private static bool ConditionCurrentlyMet(string condition, string? tag, Monster monster) => condition switch
    {
        "TargetUndamaged" => monster.Health.Current == monster.Health.Max,
        "TargetBelow25Percent" => monster.Health.Current <= monster.Health.Max * 0.25,
        "TargetTagged" => tag is not null && monster.HasTag(tag),
        _ => false,
    };

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
        // Wanderer trait — considers a jump far more often, and reaches further when it does.
        var isWanderer = npc.Trait == CreatureTraitKind.Wanderer;
        var maxHop = isWanderer ? (int)(MaxTravelHop * WandererHopMultiplier) : MaxTravelHop;

        if (pullToAnchor && anchorYear is { } anchor && anchor != npc.CurrentYear)
        {
            var anchorChance = isWanderer
                ? Math.Min(1.0, AnchorTravelAttemptChance * WandererTravelAttemptMultiplier)
                : AnchorTravelAttemptChance;
            if (random.NextDouble() > anchorChance)
            {
                return null;
            }

            targetYear = npc.Tachyons.CanAfford(TachyonEconomy.TimeTravelCost(npc.CurrentYear, anchor))
                ? anchor
                : ClosestAffordableHopToward(npc, anchor, random, maxHop);
        }
        else
        {
            var travelChance = isWanderer
                ? Math.Min(1.0, TravelAttemptChance * WandererTravelAttemptMultiplier)
                : TravelAttemptChance;
            if (random.NextDouble() > travelChance)
            {
                return null;
            }

            var hop = MinTravelHop + (int)(random.NextDouble() * (maxHop - MinTravelHop + 1));
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
    private static int ClosestAffordableHopToward(Traveler npc, int anchor, IRandomSource random, int maxHop = MaxTravelHop)
    {
        var direction = anchor > npc.CurrentYear ? 1 : -1;
        var hop = MinTravelHop + (int)(random.NextDouble() * (maxHop - MinTravelHop + 1));
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
        // Trader trait — much more likely to consider buying a slot.
        var purchaseChance = npc.Trait == CreatureTraitKind.Trader
            ? Math.Min(1.0, StorePurchaseChance * TraderStoreActivityMultiplier)
            : StorePurchaseChance;

        var vacant = storeSlots.Where(s => s.IsAvailableForPurchase).ToList();
        if (vacant.Count < 2 || random.NextDouble() >= purchaseChance)
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
    /// Credit maintenance (<see cref="Store.ApplyMaintenanceTick"/> is
    /// what actually draws it down each world tick — this just tops the
    /// reserve up so foreclosure doesn't creep closer), lists a piece of
    /// its own class-relevant surplus gear for sale (only while the shelf
    /// is under <see cref="StoreStockSoftCap"/>), marks down its oldest
    /// unsold listing when the shelf is over that cap, or collects
    /// accumulated Capital into Credits — docs/GDD.md §7's "occasionally
    /// visit/stock a store it owns." At most one action per tick, same
    /// "one action" rule as everywhere else; maintenance is checked first
    /// since an unpaid store risks repossession.
    /// </summary>
    /// <remarks>
    /// Unlike the old behavior, junk and off-class gear are never stocked
    /// here — an NPC's own shopfront should read as "themed" to its class
    /// (see <see cref="SelectClassRelevantSurplus"/>), not a dumping ground
    /// for whatever it happens to be carrying. Junk and off-class surplus
    /// are still liquidated, just not showcased here — see
    /// <see cref="TryTrade"/>, which sells them at whichever store is at
    /// hand instead.
    ///
    /// One tending action does at most one of, in priority order: (1) pay
    /// down Credit maintenance, since an unpaid store risks repossession;
    /// (2) fund Capital toward <see cref="CapitalReserveTarget"/>, since an
    /// under-capitalized store can't do its job — it can sell listings but
    /// can't buy from a traveler — at all; (3) stock a class-relevant
    /// surplus item; (4) mark down the oldest listing if over-stocked; (5)
    /// collect profit ABOVE the Capital reserve floor into Credits.
    /// </remarks>
    private static NpcTickResult? TryTendOwnStore(Traveler npc, StoreSlot ownedSlot, IRandomSource random)
    {
        // Trader trait — much more likely to bother tending this tick.
        var tendChance = npc.Trait == CreatureTraitKind.Trader
            ? Math.Min(1.0, StoreTendChance * TraderStoreActivityMultiplier)
            : StoreTendChance;
        if (random.NextDouble() >= tendChance)
        {
            return null;
        }

        var store = ownedSlot.Store!;

        if (store.CreditReserve < MaintenanceReserveTarget && npc.Credits >= MaintenanceTopUpCredits)
        {
            store.Charge(npc, MaintenanceTopUpCredits);
            return new NpcTickResult(npc.Name, NpcGoal.OwnStore, Detail: $"paid maintenance at {store.Name}");
        }

        if (store.Capital < CapitalReserveTarget && npc.Credits >= CapitalTopUpAmount)
        {
            store.Deposit(npc, CapitalTopUpAmount);
            return new NpcTickResult(npc.Name, NpcGoal.OwnStore, Detail: $"deposited {CapitalTopUpAmount} Credits into {store.Name}");
        }

        // Trader trait — a growing stocking ceiling as it levels up (per the
        // user's original request), instead of the flat StoreStockSoftCap
        // every other owner settles around.
        var stockCap = npc.Trait == CreatureTraitKind.Trader
            ? Math.Min(Store.MaxListings, StoreStockSoftCap + (int)(npc.Level * TraderStockCapPerLevel))
            : StoreStockSoftCap;

        if (store.Listings.Count < stockCap && SelectClassRelevantSurplus(npc) is { } surplus)
        {
            var price = EconomyPricing.DefaultAskingPrice(surplus);
            if (store.Deposit(npc, surplus, price))
            {
                return new NpcTickResult(npc.Name, NpcGoal.OwnStore, Detail: $"stocked {surplus.Name} at {store.Name} for {price} Credits");
            }
        }

        // Shelf is at/over the soft cap and stock isn't moving — mark down
        // the oldest unsold listing. This is the drain that keeps an owner
        // who out-produces demand from climbing to Store.MaxListings and
        // staying there (playtest: NPC shopfronts only ever grew). Firing
        // at the cap (not just above it) means a maxed shelf keeps turning
        // over — old stock out, fresh surplus in — instead of freezing.
        if (store.Listings.Count >= stockCap && random.NextDouble() < StoreClearanceChance
            && store.ClearOldestListing() is { } cleared)
        {
            return new NpcTickResult(npc.Name, NpcGoal.OwnStore, Detail: $"marked down unsold {cleared.Name} at {store.Name}");
        }

        if (store.Capital > CapitalReserveTarget)
        {
            var collected = store.CollectCapital(npc, store.Capital - CapitalReserveTarget);
            return new NpcTickResult(npc.Name, NpcGoal.OwnStore, Detail: $"collected {collected} Credits from {store.Name}");
        }

        return null;
    }

    /// <summary>
    /// Picks the single most "important to their class" piece of surplus
    /// gear an NPC is carrying, for stocking at its own store (see
    /// <see cref="TryTendOwnStore"/>) — the strongest wieldable, non-Time
    /// Shard, unequipped item that's either class-agnostic or restricted
    /// to this NPC's own class (<see cref="Item.IsClassCompatible"/>,
    /// matching docs/GDD.md §4.3's "off-class gear works at a penalty, not
    /// a hard block" framing turned into a curation signal). Off-class
    /// gear and junk never qualify — null if nothing does.
    /// </summary>
    private static Item? SelectClassRelevantSurplus(Traveler npc) =>
        npc.Inventory
            .Where(i => i.IsWieldable && !i.IsTimeShard && !IsEquipped(npc, i) && i.IsClassCompatible(npc.Class))
            .OrderByDescending(i => i.Type == ItemType.Armor ? i.DefenseBonus : i.AttackBonus)
            .FirstOrDefault();

    /// <summary>
    /// Sells one piece of surplus gear (a looted Weapon/Armor/Ranged item
    /// that isn't equipped — <see cref="Act"/> already claimed anything
    /// that would've been an upgrade, so what's left is genuine dead
    /// weight) or excess junk, or — the demand side of the economy — buys
    /// one listed item it can afford: a real upgrade for a slot, or
    /// (sometimes) a consumable. At most one action, at a randomly picked
    /// store. Selling gear is tried first: it's what actually makes a store
    /// worth browsing, junk is just Credit filler. Buying is what keeps
    /// listing counts from climbing forever — <see cref="Store.SellToTraveler"/>
    /// removes the listing. Null if there was nothing worth doing (falls
    /// through to grinding). Called by <see cref="TryStoreVisit"/> with
    /// every occupied store here — this itself has no notion of
    /// ownership/vacancy.
    /// </summary>
    private static NpcTickResult? TryTrade(Traveler npc, IReadOnlyList<Store> stores, IRandomSource random)
    {
        var surplusGear = npc.Inventory.FirstOrDefault(i => i.IsWieldable && !i.IsTimeShard && !IsEquipped(npc, i));
        var junkCount = npc.Inventory.Count(i => i.Type == ItemType.Junk);
        // Hoarder trait — never voluntarily sells anything; keeps collecting instead.
        var isHoarder = npc.Trait == CreatureTraitKind.Hoarder;
        var wantsToSellGear = !isHoarder && surplusGear is not null;
        var wantsToSellJunk = !isHoarder && junkCount > ExcessJunkThreshold;
        var wantsToBuyWeapon = npc.EquippedWeapon is null && npc.Credits > 0;
        var wantsToShop = npc.EquippedWeapon is not null && npc.Credits > 0
                          && npc.Inventory.Count < Traveler.MaxInventorySize;

        if (!wantsToSellGear && !wantsToSellJunk && !wantsToBuyWeapon && !wantsToShop)
        {
            return null;
        }

        var store = stores[(int)(random.NextDouble() * stores.Count)];

        if (wantsToSellGear)
        {
            var price = store.BuyFromTraveler(npc, surplusGear!);
            if (price is not null)
            {
                ApplyScavengerSellBonus(npc, price.Value);
                return new NpcTickResult(npc.Name, NpcGoal.Trade, Detail: $"sold {surplusGear!.Name} to {store.Name} for {price} Credits");
            }
        }

        if (wantsToSellJunk)
        {
            var junk = npc.Inventory.First(i => i.Type == ItemType.Junk);
            var price = store.BuyFromTraveler(npc, junk);
            if (price is not null)
            {
                ApplyScavengerSellBonus(npc, price.Value);
                return new NpcTickResult(npc.Name, NpcGoal.Trade, Detail: $"sold {junk.Name} to {store.Name} for {price} Credits");
            }
        }

        if (wantsToBuyWeapon)
        {
            var affordable = store.Listings
                .Where(l => l.Item.Type == ItemType.Weapon && l.AskingPrice <= npc.Credits)
                .OrderBy(l => l.AskingPrice)
                .FirstOrDefault();

            // SellToTraveler can fail without buying anything (pack already
            // full) — must check it before Wield, same as the wantsToShop
            // branch below, or Wield throws for an item that was never
            // actually added to the NPC's inventory (this crashed the game:
            // "'Rusted Shiv' is not in <npc>'s inventory.").
            if (affordable is not null && store.SellToTraveler(npc, affordable))
            {
                npc.Wield(affordable.Item);
                return new NpcTickResult(npc.Name, NpcGoal.Trade, Detail: $"bought and wielded {affordable.Item.Name} from {store.Name}");
            }
        }

        if (wantsToShop && random.NextDouble() < ShopPurchaseChance)
        {
            if (PickPurchase(npc, stores, random) is (var buyStore, var listing)
                && buyStore.SellToTraveler(npc, listing))
            {
                var bought = listing.Item;
                if (bought.IsWieldable && !bought.IsTimeShard && !IsEquipped(npc, bought))
                {
                    npc.Wield(bought);
                    return new NpcTickResult(npc.Name, NpcGoal.Trade, Detail: $"bought and wielded {bought.Name} from {buyStore.Name} for {listing.AskingPrice} Credits");
                }

                return new NpcTickResult(npc.Name, NpcGoal.Trade, Detail: $"bought {bought.Name} from {buyStore.Name} for {listing.AskingPrice} Credits");
            }
        }

        return null; // wanted to trade but nothing worked out this tick
    }

    /// <summary>Scavenger trait — a bonus fraction of Credits kept on top of a store sale, on top of whatever the store paid — mirrors <see cref="Monsters.Monster"/>'s Scavenger Convert bonus.</summary>
    private static void ApplyScavengerSellBonus(Traveler npc, int price)
    {
        if (npc.Trait != CreatureTraitKind.Scavenger || price <= 0)
        {
            return;
        }

        var bonus = (int)Math.Round(price * ScavengerSellBonusPct);
        if (bonus > 0)
        {
            npc.AddCredits(bonus);
        }
    }

    /// <summary>
    /// Chooses one listing an NPC will buy this tick, or null. Prefers a
    /// genuine slot upgrade — a class-compatible Weapon/Armor/Ranged whose
    /// bonus (scaled by <see cref="Item.WieldEffectiveness"/>, the same bar
    /// <see cref="FindUpgrade"/> uses) beats what the NPC has equipped in
    /// that slot right now — picking the cheapest such listing across every
    /// occupied store here. Failing that, sometimes a consumable, as Credit
    /// filler that still drains a listing. The NPC's own store is skipped —
    /// buying your own stock is pointless churn.
    /// </summary>
    private static (Store Store, StoreListing Listing)? PickPurchase(Traveler npc, IReadOnlyList<Store> stores, IRandomSource random)
    {
        (Store Store, StoreListing Listing)? bestUpgrade = null;
        var bestUpgradePrice = int.MaxValue;
        var consumables = new List<(Store Store, StoreListing Listing)>();

        foreach (var store in stores)
        {
            if (store.Owner == npc)
            {
                continue;
            }

            foreach (var listing in store.Listings)
            {
                if (listing.AskingPrice > npc.Credits)
                {
                    continue;
                }

                var item = listing.Item;
                if (item.IsWieldable && !item.IsTimeShard && item.IsClassCompatible(npc.Class)
                    && !(item.IsRanged && item.IsDepleted))
                {
                    var current = item.Type switch
                    {
                        ItemType.Weapon => npc.EquippedWeapon is { } w ? w.AttackBonus * w.WieldEffectiveness(npc.Class) : 0,
                        ItemType.Armor => npc.EquippedArmor is { } a ? a.DefenseBonus * a.WieldEffectiveness(npc.Class) : 0,
                        ItemType.Ranged => npc.EquippedRanged is { } r ? r.AttackBonus * r.WieldEffectiveness(npc.Class) : 0,
                        _ => double.MaxValue,
                    };
                    var offered = (item.Type == ItemType.Armor ? item.DefenseBonus : item.AttackBonus)
                                  * item.WieldEffectiveness(npc.Class);

                    if (offered > current && listing.AskingPrice < bestUpgradePrice)
                    {
                        bestUpgrade = (store, listing);
                        bestUpgradePrice = listing.AskingPrice;
                    }
                }
                else if (item.Type == ItemType.Consumable && !item.IsTimeShard)
                {
                    consumables.Add((store, listing));
                }
            }
        }

        if (bestUpgrade is not null)
        {
            return bestUpgrade;
        }

        if (consumables.Count > 0 && random.NextDouble() < 0.5)
        {
            return consumables[(int)(random.NextDouble() * consumables.Count)];
        }

        return null;
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
