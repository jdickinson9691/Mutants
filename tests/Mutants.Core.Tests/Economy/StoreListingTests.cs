using Mutants.Core.Economy;
using Mutants.Core.Items;

namespace Mutants.Core.Tests.Economy;

public class StoreListingTests
{
    [Fact]
    public void Constructor_RejectsNonPositiveAskingPrice()
    {
        var item = Item.Create("Scrap", ItemType.Junk, 1, Rarity.Common);
        Assert.Throws<ArgumentOutOfRangeException>(() => new StoreListing(item, 0));
    }
}
