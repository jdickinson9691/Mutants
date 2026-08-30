using Mutants.Core.Economy;
using Mutants.Core.World;

namespace Mutants.Core.Tests.Economy;

public class TestStoresTests
{
    [Fact]
    public void Build_ProducesThreeSlots()
    {
        Assert.Equal(3, TestStores.Build().Count);
    }

    [Fact]
    public void Build_TwoGovernmentStoresArePreSeededWithListings()
    {
        var slots = TestStores.Build();
        var seeded = slots.Where(s => !s.IsAvailableForPurchase).ToList();

        Assert.Equal(2, seeded.Count);
        Assert.All(seeded, s => Assert.True(s.Store!.IsGovernmentRun));
        Assert.All(seeded, s => Assert.NotEmpty(s.Store!.Listings));
    }

    [Fact]
    public void Build_OneSlotIsAvailableForPurchase()
    {
        var slots = TestStores.Build();
        Assert.Single(slots, s => s.IsAvailableForPurchase);
    }

    [Fact]
    public void Build_EverySlotSitsInARoomThatExistsOnTheTestLevel()
    {
        var level = TestLevel.Build();
        var slots = TestStores.Build();

        Assert.All(slots, slot => Assert.NotNull(level.TryGetRoom(slot.Location)));
    }
}
