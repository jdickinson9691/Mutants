using ChronoTravelers.Core.Items;

namespace ChronoTravelers.Core.Economy;

/// <summary>One item a <see cref="Store"/> has for sale, at a specific asking price.</summary>
public sealed record StoreListing
{
    public Item Item { get; }
    public int AskingPrice { get; }

    public StoreListing(Item item, int askingPrice)
    {
        if (askingPrice < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(askingPrice), askingPrice, "Asking price must be positive.");
        }

        Item = item;
        AskingPrice = askingPrice;
    }
}
