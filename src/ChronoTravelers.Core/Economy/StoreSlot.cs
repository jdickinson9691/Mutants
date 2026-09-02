using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.World;

namespace ChronoTravelers.Core.Economy;

/// <summary>
/// A store location in the world — docs/GDD.md §6.2: "a player ... can
/// purchase an available government-built store slot in a level's city."
/// A null <see cref="Store"/> means the slot is unclaimed and available to
/// purchase; a pre-seeded government store or a purchased player store
/// both just mean <see cref="Store"/> is populated.
/// </summary>
public sealed class StoreSlot
{
    public string Name { get; }
    public Coordinate Location { get; }
    public int HomeLevel { get; }
    public int PurchaseCost { get; }
    public Store? Store { get; private set; }

    public bool IsAvailableForPurchase => Store is null;

    /// <summary>
    /// Listings left behind by a store repossessed here for unpaid
    /// maintenance (see <see cref="Repossess"/>) — carried forward and
    /// re-stocked into whoever buys the slot next (<see cref="Purchase"/>),
    /// per docs/GDD.md §6.2: unpaid upkeep causes "stores, and their
    /// inventories, to eventually become for sale."
    /// </summary>
    private readonly List<StoreListing> _abandonedListings = [];

    /// <summary>True if this vacant slot still carries inventory from a repossessed store, waiting for the next buyer.</summary>
    public bool HasAbandonedInventory => _abandonedListings.Count > 0;

    public StoreSlot(string name, Coordinate location, int homeLevel, int purchaseCost, Store? store = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name cannot be empty.", nameof(name));
        }

        if (homeLevel < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(homeLevel), homeLevel, "Home level must be at least 1.");
        }

        if (purchaseCost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(purchaseCost), purchaseCost, "Purchase cost cannot be negative.");
        }

        Name = name;
        Location = location;
        HomeLevel = homeLevel;
        PurchaseCost = purchaseCost;
        Store = store;
    }

    /// <summary>
    /// Buys this slot for <paramref name="buyer"/> — docs/GDD.md §6.2.
    /// Spends <see cref="PurchaseCost"/> Credits and seeds the new store
    /// with <paramref name="startingCapital"/> (an original placeholder —
    /// the GDD doesn't specify how a fresh player store is capitalized;
    /// depositing more Credits into an existing store isn't modeled yet).
    /// Throws if the slot is already occupied.
    /// </summary>
    public Store Purchase(Traveler buyer, int startingCapital = 100, int startingTachyonReserve = 50)
    {
        if (Store is not null)
        {
            throw new InvalidOperationException($"'{Name}' is already occupied.");
        }

        buyer.SpendCredits(PurchaseCost);
        Store = new Store($"{buyer.Name}'s Store", HomeLevel, startingCapital, buyer, startingTachyonReserve);

        foreach (var listing in _abandonedListings)
        {
            Store.Stock(listing.Item, listing.AskingPrice);
        }

        _abandonedListings.Clear();
        return Store;
    }

    /// <summary>
    /// Reclaims this slot after its owner falls <see cref="Store.ForeclosureThreshold"/>
    /// consecutive ticks behind on Tachyon maintenance (see
    /// <see cref="Store.ApplyMaintenanceTick"/>, driven by
    /// ChronoTravelers.Engine.Simulation.WorldSimulation's maintenance pass) — docs/GDD.md
    /// §6.2: unpaid upkeep eventually "causes stores, and their
    /// inventories, to become for sale." The slot goes back up for
    /// purchase; its former listings are held (<see cref="HasAbandonedInventory"/>)
    /// and re-stocked into whoever buys it next via <see cref="Purchase"/>
    /// rather than lost outright. Capital and any leftover Tachyon reserve
    /// are forfeited. Throws on a government store (exempt from
    /// maintenance — see <see cref="Store.IsGovernmentRun"/>) or an
    /// already-vacant slot.
    /// </summary>
    public void Repossess()
    {
        if (Store is not { IsGovernmentRun: false } store)
        {
            throw new InvalidOperationException($"'{Name}' has no owned store to repossess.");
        }

        _abandonedListings.AddRange(store.Listings);
        Store = null;
    }

    /// <summary>
    /// Re-attaches a store <paramref name="owner"/> bought in a previous
    /// session — no Credit charge, capital and Tachyon reserve restored
    /// as-is. Persistence only (see
    /// ChronoTravelers.Engine.Persistence.CharacterMapper); players buy
    /// in through <see cref="Purchase"/>. The caller re-stocks listings
    /// via <see cref="Store.Stock"/> afterward. Throws if the slot is
    /// already occupied.
    /// </summary>
    public Store RestoreOwnership(Traveler owner, int capital, int tachyonReserve = 0)
    {
        if (Store is not null)
        {
            throw new InvalidOperationException($"'{Name}' is already occupied.");
        }

        Store = new Store($"{owner.Name}'s Store", HomeLevel, Math.Max(0, capital), owner, Math.Max(0, tachyonReserve));
        return Store;
    }
}
