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
    private const double BuyMargin = 0.7;
    private const double MarkupMargin = 1.3;

    /// <summary>Tachyons a player/NPC-owned store's maintenance draws per world tick, per unit of the store's year-tier (see Time.TimeScale.TierForYear) — a later-year store costs more to keep open. Government stores are exempt (Store.IsGovernmentRun); original tuning, not GDD-specified.</summary>
    private const double MaintenanceCostPerTier = 1.0;

    /// <summary>What a store pays a seller for an item.</summary>
    public static int BuyPrice(Item item) => Math.Max(1, (int)Math.Round(item.Value * BuyMargin));

    /// <summary>What a store asks when reselling an item it just bought, or when initially stocked.</summary>
    public static int DefaultAskingPrice(Item item) => Math.Max(1, (int)Math.Round(item.Value * MarkupMargin));

    /// <summary>Tachyon maintenance one player/NPC-owned store owes for a single world tick, for a store sitting at <paramref name="tier"/> (Time.TimeScale.TierForYear(store.HomeLevel)) — docs/GDD.md §6.2. Never less than 1, so upkeep is never free even in year 2000.</summary>
    public static int MaintenanceCostPerTick(double tier) => Math.Max(1, (int)Math.Round(tier * MaintenanceCostPerTier));
}
