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
    public Store Purchase(Traveler buyer, int startingCapital = 100)
    {
        if (Store is not null)
        {
            throw new InvalidOperationException($"'{Name}' is already occupied.");
        }

        buyer.SpendCredits(PurchaseCost);
        Store = new Store($"{buyer.Name}'s Store", HomeLevel, startingCapital, buyer);
        return Store;
    }

    /// <summary>
    /// Re-attaches a store <paramref name="owner"/> bought in a previous
    /// session — no Credit charge, capital restored as-is. Persistence
    /// only (see ChronoTravelers.Engine.Persistence.CharacterMapper); players buy
    /// in through <see cref="Purchase"/>. The caller re-stocks listings
    /// via <see cref="Store.Stock"/> afterward. Throws if the slot is
    /// already occupied.
    /// </summary>
    public Store RestoreOwnership(Traveler owner, int capital)
    {
        if (Store is not null)
        {
            throw new InvalidOperationException($"'{Name}' is already occupied.");
        }

        Store = new Store($"{owner.Name}'s Store", HomeLevel, Math.Max(0, capital), owner);
        return Store;
    }
}
