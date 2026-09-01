namespace ChronoTravelers.Core.Time;

/// <summary>
/// The tuning knobs for a year's stores — how a government store is
/// stocked and what an empty player slot costs. Kept tiny on purpose: the
/// government store always carries the same <em>kinds</em> of staple
/// (a heal item, an attack potion, a defense potion, a weapon, an armour
/// piece), picked from the year's era themes by <see cref="TimeWorld"/>;
/// only the numbers here are content-authored. Loaded from
/// <c>store-templates.json</c>.
/// </summary>
public sealed record StoreStockTemplate(
    int PlayerSlotBaseCost,
    int PlayerSlotCostPerTier)
{
    public static StoreStockTemplate Default { get; } = new(PlayerSlotBaseCost: 100, PlayerSlotCostPerTier: 110);

    /// <summary>Credit cost of buying an empty player store slot in a year of the given whole-number tier.</summary>
    public int PlayerSlotCostForTier(int tier) =>
        PlayerSlotBaseCost + PlayerSlotCostPerTier * Math.Max(0, tier - 1);
}
