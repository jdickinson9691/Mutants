using ChronoTravelers.Core.Items;

namespace ChronoTravelers.Core.Economy;

/// <summary>
/// Store buy/sell margins. docs/GDD.md §6 confirms sell price is
/// "store-and-negotiation-dependent" but gives no formula — these margins
/// are original tuning pending Design Agent sign-off. A store buys below
/// an item's value and resells above it, so Credits are destroyed as
/// goods cycle through a store — one of the docs/GDD.md §6.3 "Credit
/// sinks" the economy needs so currency doesn't purely inflate.
/// </summary>
public static class EconomyPricing
{
    /// <summary>
    /// Multiplier applied to an item's own <see cref="Item.Value"/> when
    /// sold directly to any store via the <c>sell</c> command
    /// (<see cref="Item.SellValue"/>) — docs/GDD.md §5 previously called
    /// this out as "a flat 1:1-with-Value placeholder" pending real
    /// negotiation-based pricing. Bumped +30% (original tuning): selling
    /// loot is the primary Credit faucet, and 1:1 undersold it relative to
    /// what a store turns around and resells for. <see cref="BuyMargin"/>/
    /// <see cref="MarkupMargin"/> below scale by this same factor so an
    /// NPC selling into a player/NPC-owned store's resale shelf
    /// (<see cref="Store.BuyFromTraveler"/>) still pays proportionally
    /// less than a direct sale, and that store's own markup
    /// (<see cref="DefaultAskingPrice"/>) still ends up proportionally
    /// above what it paid — the relative spread this Credit-sink margin
    /// depends on (docs/GDD.md §6.3) is unchanged, just uniformly scaled up.
    /// </summary>
    public const double SellRateMultiplier = 1.3;

    private const double BuyMargin = 0.7 * SellRateMultiplier;
    private const double MarkupMargin = 1.3 * SellRateMultiplier;

    /// <summary>Credits a player/NPC-owned store's maintenance draws per world tick, per unit of the store's year-tier (see Time.TimeScale.TierForYear) — a later-year store costs more to keep open. Government stores are exempt (Store.IsGovernmentRun); original tuning, not GDD-specified. Originally a Tachyon cost; switched to Credits so a store's upkeep draws on the same currency its Capital/sales do, rather than competing with the owner's own survival/travel/heal Tachyon budget.</summary>
    private const double MaintenanceCostPerTier = 1.0;

    /// <summary>What a store pays a seller for an item.</summary>
    public static int BuyPrice(Item item) => Math.Max(1, (int)Math.Round(item.Value * BuyMargin));

    /// <summary>What a store asks when reselling an item it just bought, or when initially stocked.</summary>
    public static int DefaultAskingPrice(Item item) => Math.Max(1, (int)Math.Round(item.Value * MarkupMargin));

    /// <summary>Credit maintenance one player/NPC-owned store owes for a single world tick, for a store sitting at <paramref name="tier"/> (Time.TimeScale.TierForYear(store.HomeLevel)) — docs/GDD.md §6.2. Never less than 1, so upkeep is never free even in year 2000.</summary>
    public static int MaintenanceCostPerTick(double tier) => Math.Max(1, (int)Math.Round(tier * MaintenanceCostPerTier));
}
