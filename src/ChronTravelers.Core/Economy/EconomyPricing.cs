using ChronTravelers.Core.Items;

namespace ChronTravelers.Core.Economy;

/// <summary>
/// Store buy/sell margins. docs/GDD.md §6 confirms sell price is
/// "store-and-negotiation-dependent" but gives no formula — these margins
/// are original tuning pending Design Agent sign-off. A store buys below
/// an item's value and resells above it, so Riblets are destroyed as
/// goods cycle through a store — one of the docs/GDD.md §6.3 "Riblet
/// sinks" the economy needs so currency doesn't purely inflate.
/// </summary>
public static class EconomyPricing
{
    private const double BuyMargin = 0.7;
    private const double MarkupMargin = 1.3;

    /// <summary>What a store pays a seller for an item.</summary>
    public static int BuyPrice(Item item) => Math.Max(1, (int)Math.Round(item.Value * BuyMargin));

    /// <summary>What a store asks when reselling an item it just bought, or when initially stocked.</summary>
    public static int DefaultAskingPrice(Item item) => Math.Max(1, (int)Math.Round(item.Value * MarkupMargin));
}
