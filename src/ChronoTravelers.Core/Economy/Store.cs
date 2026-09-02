using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Items;

namespace ChronoTravelers.Core.Economy;

/// <summary>
/// A store — government-run (docs/GDD.md §6.1, <see cref="IsGovernmentRun"/>
/// true, Owner null) or player-owned (§6.2, after a <see cref="StoreSlot"/>
/// is purchased). Deposit/withdraw/price changes are owner-only per §6.2
/// ("the player stocks it manually"); buying/selling with the store is
/// open to anyone. §6.3's "budget cap per visit" safeguard is enforced by
/// the caller (ChronoTravelers.Engine.Npc.NpcController for NPC shoppers) rather
/// than here.
/// </summary>
public sealed class Store
{
    public string Name { get; }
    public int HomeLevel { get; }
    public Traveler? Owner { get; }
    public int Capital { get; private set; }

    /// <summary>
    /// Tachyons on hand to pay this store's per-tick maintenance — docs/GDD.md
    /// §6.2's new "charge" command tops this up (<see cref="Charge"/>);
    /// <see cref="ApplyMaintenanceTick"/> draws it down each world tick.
    /// Always 0 and unused for a government store (<see cref="IsGovernmentRun"/>).
    /// </summary>
    public int TachyonReserve { get; private set; }

    /// <summary>Consecutive world ticks this store's maintenance has gone unpaid — resets to 0 the moment a tick is fully covered. See <see cref="ApplyMaintenanceTick"/> and <see cref="ForeclosureThreshold"/>.</summary>
    public int MissedMaintenanceTicks { get; private set; }

    /// <summary>How many consecutive underfunded maintenance ticks a player/NPC-owned store tolerates before it's repossessed (docs/GDD.md §6.2: "eventually become for sale") — see StoreSlot.Repossess, driven by the WorldSimulation maintenance pass.</summary>
    public const int ForeclosureThreshold = 10;

    public bool IsGovernmentRun => Owner is null;

    private readonly List<StoreListing> _listings = [];
    public IReadOnlyList<StoreListing> Listings => _listings;

    public Store(string name, int homeLevel, int startingCapital, Traveler? owner = null, int startingTachyonReserve = 0)
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

        if (startingTachyonReserve < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startingTachyonReserve), startingTachyonReserve, "Starting Tachyon reserve cannot be negative.");
        }

        Name = name;
        HomeLevel = homeLevel;
        Capital = startingCapital;
        Owner = owner;
        TachyonReserve = startingTachyonReserve;
    }

    /// <summary>A government store — docs/GDD.md §6.1. Effectively unlimited capital, representing government backing.</summary>
    public static Store CreateGovernmentStore(string name, int homeLevel) =>
        new(name, homeLevel, startingCapital: 1_000_000);

    /// <summary>
    /// Stocks a listing directly, with no owner or payment involved — for
    /// world/content setup (see ChronoTravelers.Core.Economy.TestStores), not
    /// player action. Use <see cref="Deposit"/> for an owner selling their
    /// own inventory.
    /// </summary>
    public void Stock(Item item, int askingPrice) => _listings.Add(new StoreListing(item, askingPrice));

    /// <summary>
    /// The store buys an item from <paramref name="seller"/> for Credits
    /// (docs/GDD.md §5/§6), immediately re-listing it for resale. Returns
    /// null (and does nothing) if the store's Capital can't cover the
    /// price — the §6.3 Credit-sink safeguard in action; government
    /// stores' huge Capital means this practically never happens to them.
    /// </summary>
    public int? BuyFromTraveler(Traveler seller, Item item)
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

    /// <summary>A traveler buys a listed item from the store for Credits.</summary>
    public void SellToTraveler(Traveler buyer, StoreListing listing)
    {
        if (!_listings.Remove(listing))
        {
            throw new InvalidOperationException($"'{listing.Item.Name}' is not for sale at {Name}.");
        }

        buyer.SpendCredits(listing.AskingPrice);
        buyer.AddToInventory(listing.Item);
        Capital += listing.AskingPrice;
    }

    /// <summary>Owner deposits an item from their own inventory for sale — docs/GDD.md §6.2's "stock" command (the item-listing half of what used to be called "deposit"). Owner-only.</summary>
    public void Deposit(Traveler owner, Item item, int askingPrice)
    {
        RequireOwner(owner);
        owner.RemoveFromInventory(item);
        Stock(item, askingPrice);
    }

    /// <summary>
    /// Owner deposits Credits from their own pocket into the store's
    /// Capital — docs/GDD.md §6.2's "deposit" command (the funding half;
    /// see the Item overload above for the item-listing half, now "stock").
    /// Capital is what pays for <see cref="BuyFromTraveler"/> purchases
    /// from other travelers, so a store an owner never funds can only ever
    /// resell what it's stocked directly. Owner-only.
    /// </summary>
    public void Deposit(Traveler owner, int credits)
    {
        RequireOwner(owner);
        if (credits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(credits), credits, "Must deposit a positive amount.");
        }

        owner.SpendCredits(credits);
        Capital += credits;
    }

    /// <summary>Owner pulls a listed item back into their own inventory, unlisting it. Owner-only.</summary>
    public void Withdraw(Traveler owner, StoreListing listing)
    {
        RequireOwner(owner);
        if (!_listings.Remove(listing))
        {
            throw new InvalidOperationException($"'{listing.Item.Name}' is not listed at {Name}.");
        }

        owner.AddToInventory(listing.Item);
    }

    /// <summary>Owner changes a listing's asking price. Owner-only.</summary>
    public void AdjustPrice(Traveler owner, StoreListing listing, int newPrice)
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
    /// into their personal Credits — the "idle-income loop" of
    /// docs/GDD.md §6.2. Owner-only.
    /// </summary>
    public int CollectCapital(Traveler owner, int amount)
    {
        RequireOwner(owner);
        if (amount < 0 || amount > Capital)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, $"Can't collect {amount}; {Name} holds {Capital}.");
        }

        Capital -= amount;
        owner.AddCredits(amount);
        return amount;
    }

    /// <summary>
    /// Owner tops up <see cref="TachyonReserve"/> with their own Tachyons —
    /// docs/GDD.md §6.2's "charge" command, the counterpart to
    /// <see cref="ApplyMaintenanceTick"/> drawing it down. Owner-only.
    /// Spends the owner's Tachyons directly (throws if they can't afford
    /// it — callers should check <c>owner.Tachyons.CanAfford</c> first, the
    /// same convention as everywhere else Tachyons are spent).
    /// </summary>
    public void Charge(Traveler owner, int tachyons)
    {
        RequireOwner(owner);
        if (tachyons <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tachyons), tachyons, "Must charge a positive amount.");
        }

        owner.Tachyons.Spend(tachyons);
        TachyonReserve += tachyons;
    }

    /// <summary>
    /// One world tick's maintenance draw — docs/GDD.md §6.2. A government
    /// store (<see cref="IsGovernmentRun"/>) is exempt and this is always a
    /// no-op for one. Otherwise <paramref name="cost"/> Tachyons come out
    /// of <see cref="TachyonReserve"/>; if the reserve can't cover it, it's
    /// drained to 0 and a missed tick is recorded instead of a partial
    /// payment. Paying a tick in full resets <see cref="MissedMaintenanceTicks"/>
    /// back to 0. Returns true once <see cref="ForeclosureThreshold"/>
    /// consecutive misses is reached — the caller (WorldSimulation) is
    /// responsible for actually reclaiming the slot via
    /// <see cref="StoreSlot.Repossess"/>; this method only tracks the streak.
    /// </summary>
    public bool ApplyMaintenanceTick(int cost)
    {
        if (IsGovernmentRun)
        {
            return false;
        }

        if (TachyonReserve >= cost)
        {
            TachyonReserve -= cost;
            MissedMaintenanceTicks = 0;
            return false;
        }

        TachyonReserve = 0;
        MissedMaintenanceTicks++;
        return MissedMaintenanceTicks >= ForeclosureThreshold;
    }

    private void RequireOwner(Traveler traveler)
    {
        if (Owner != traveler)
        {
            throw new InvalidOperationException($"{traveler.Name} does not own {Name}.");
        }
    }
}
