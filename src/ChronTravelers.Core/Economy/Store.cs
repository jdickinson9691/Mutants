using ChronTravelers.Core.Characters;
using ChronTravelers.Core.Items;

namespace ChronTravelers.Core.Economy;

/// <summary>
/// A store — government-run (docs/GDD.md §6.1, <see cref="IsGovernmentRun"/>
/// true, Owner null) or player-owned (§6.2, after a <see cref="StoreSlot"/>
/// is purchased). Deposit/withdraw/price changes are owner-only per §6.2
/// ("the player stocks it manually"); buying/selling with the store is
/// open to anyone. §6.3's "budget cap per visit" safeguard is enforced by
/// the caller (ChronTravelers.Engine.Npc.NpcController for NPC shoppers) rather
/// than here.
/// </summary>
public sealed class Store
{
    public string Name { get; }
    public int HomeLevel { get; }
    public Mutant? Owner { get; }
    public int Capital { get; private set; }

    public bool IsGovernmentRun => Owner is null;

    private readonly List<StoreListing> _listings = [];
    public IReadOnlyList<StoreListing> Listings => _listings;

    public Store(string name, int homeLevel, int startingCapital, Mutant? owner = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name cannot be empty.", nameof(name));
        }

        if (homeLevel < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(homeLevel), homeLevel, "Home level must be at least 1.");
        }

        if (startingCapital < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startingCapital), startingCapital, "Starting capital cannot be negative.");
        }

        Name = name;
        HomeLevel = homeLevel;
        Capital = startingCapital;
        Owner = owner;
    }

    /// <summary>A government store — docs/GDD.md §6.1. Effectively unlimited capital, representing government backing.</summary>
    public static Store CreateGovernmentStore(string name, int homeLevel) =>
        new(name, homeLevel, startingCapital: 1_000_000);

    /// <summary>
    /// Stocks a listing directly, with no owner or payment involved — for
    /// world/content setup (see ChronTravelers.Core.Economy.TestStores), not
    /// player action. Use <see cref="Deposit"/> for an owner selling their
    /// own inventory.
    /// </summary>
    public void Stock(Item item, int askingPrice) => _listings.Add(new StoreListing(item, askingPrice));

    /// <summary>
    /// The store buys an item from <paramref name="seller"/> for Riblets
    /// (docs/GDD.md §5/§6), immediately re-listing it for resale. Returns
    /// null (and does nothing) if the store's Capital can't cover the
    /// price — the §6.3 Riblet-sink safeguard in action; government
    /// stores' huge Capital means this practically never happens to them.
    /// </summary>
    public int? BuyFromMutant(Mutant seller, Item item)
    {
        var price = EconomyPricing.BuyPrice(item);
        if (Capital < price)
        {
            return null;
        }

        seller.Sell(item, price);
        Capital -= price;
        Stock(item, EconomyPricing.DefaultAskingPrice(item));
        return price;
    }

    /// <summary>A mutant buys a listed item from the store for Riblets.</summary>
    public void SellToMutant(Mutant buyer, StoreListing listing)
    {
        if (!_listings.Remove(listing))
        {
            throw new InvalidOperationException($"'{listing.Item.Name}' is not for sale at {Name}.");
        }

        buyer.SpendRiblets(listing.AskingPrice);
        buyer.AddToInventory(listing.Item);
        Capital += listing.AskingPrice;
    }

    /// <summary>Owner deposits an item from their own inventory for sale — docs/GDD.md §6.2. Owner-only.</summary>
    public void Deposit(Mutant owner, Item item, int askingPrice)
    {
        RequireOwner(owner);
        owner.RemoveFromInventory(item);
        Stock(item, askingPrice);
    }

    /// <summary>Owner pulls a listed item back into their own inventory, unlisting it. Owner-only.</summary>
    public void Withdraw(Mutant owner, StoreListing listing)
    {
        RequireOwner(owner);
        if (!_listings.Remove(listing))
        {
            throw new InvalidOperationException($"'{listing.Item.Name}' is not listed at {Name}.");
        }

        owner.AddToInventory(listing.Item);
    }

    /// <summary>Owner changes a listing's asking price. Owner-only.</summary>
    public void AdjustPrice(Mutant owner, StoreListing listing, int newPrice)
    {
        RequireOwner(owner);
        var index = _listings.IndexOf(listing);
        if (index < 0)
        {
            throw new InvalidOperationException($"'{listing.Item.Name}' is not listed at {Name}.");
        }

        _listings[index] = new StoreListing(listing.Item, newPrice);
    }

    /// <summary>
    /// Owner withdraws accumulated Capital (from NPC/player purchases)
    /// into their personal Riblets — the "idle-income loop" of
    /// docs/GDD.md §6.2. Owner-only.
    /// </summary>
    public int CollectCapital(Mutant owner, int amount)
    {
        RequireOwner(owner);
        if (amount < 0 || amount > Capital)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, $"Can't collect {amount}; {Name} holds {Capital}.");
        }

        Capital -= amount;
        owner.AddRiblets(amount);
        return amount;
    }

    private void RequireOwner(Mutant mutant)
    {
        if (Owner != mutant)
        {
            throw new InvalidOperationException($"{mutant.Name} does not own {Name}.");
        }
    }
}
