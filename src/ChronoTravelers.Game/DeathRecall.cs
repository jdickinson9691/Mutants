using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Time;
using ChronoTravelers.Core.World;
using ChronoTravelers.Engine;

namespace ChronoTravelers.Game;

/// <summary>
/// docs/GDD.md §3.3's "death & recall," shared by both front ends so they
/// can't drift apart on it (see docs/TECH_STACK.md's note on why this
/// project exists). Dying is a setback, not game over — GDD §3.3: "Dying
/// drops a portion of unconverted inventory at the death location (loot
/// becomes lootable by other NPCs/Travelers) and snaps the character back
/// upstream to the year 2000 A.D. with a Tachyon penalty… tuned to punish
/// but not erase progress."
///
/// Before this, neither front end actually implemented it: the console
/// just ended the session on defeat (no save, straight to the title
/// screen), and the multiplayer server's <c>SharedGame.Respawn</c> only
/// did the "snap back to 2000" half — no loot drop, no Tachyon penalty,
/// full heal with no cost at all.
/// </summary>
public static class DeathRecall
{
    /// <summary>
    /// Fraction of the Traveler's *unequipped* inventory dropped at the
    /// death location, rounded down. Currently equipped weapon/armor/
    /// ranged are deliberately never candidates — "punish but not erase
    /// progress" reads as "you can still defend yourself when you come
    /// to," not "you might lose your only weapon to a bad ambush roll."
    /// </summary>
    public const double InventoryDropFraction = 0.5;

    /// <summary>Fraction of current Tachyons lost on death — the GDD's "Tachyon penalty," sized to sting without being a full wipe.</summary>
    public const double TachyonPenaltyFraction = 0.5;

    /// <summary>What happened, for each front end to narrate in its own voice.</summary>
    public sealed record Outcome(
        int DeathYear,
        Coordinate DeathPosition,
        IReadOnlyList<Item> DroppedItems,
        int TachyonsLost);

    /// <summary>
    /// Applies the recall in place: drops a portion of unequipped
    /// inventory as ground loot at the death spot (<see cref="YearPopulation.AddGroundLoot"/>
    /// — lootable by other NPCs/Travelers per the GDD, exactly like a
    /// monster's own drop), spends the Tachyon penalty, moves the
    /// Traveler to year 2000's start room, and heals to full. Level,
    /// stats, Credits, and equipped gear are untouched — this is a
    /// setback, not a rollback. Does not check <c>Health.IsDead</c>
    /// itself (some callers apply this the instant the killing blow
    /// lands, before any other state settles) — callers own that check.
    /// </summary>
    public static Outcome Apply(Traveler traveler, TimeWorld world, IRandomSource random)
    {
        var deathYear = traveler.CurrentYear;
        var deathPosition = traveler.Position;
        var population = world.GetYear(deathYear).Population;

        var droppable = traveler.Inventory
            .Where(i => !ReferenceEquals(i, traveler.EquippedWeapon)
                && !ReferenceEquals(i, traveler.EquippedArmor)
                && !ReferenceEquals(i, traveler.EquippedRanged))
            .ToList();

        var dropCount = (int)Math.Floor(droppable.Count * InventoryDropFraction);
        var dropped = new List<Item>(dropCount);
        for (var i = 0; i < dropCount; i++)
        {
            var index = Math.Min(droppable.Count - 1, (int)(random.NextDouble() * droppable.Count));
            var pick = droppable[index];
            droppable.RemoveAt(index);
            traveler.RemoveFromInventory(pick);
            population.AddGroundLoot(deathPosition, pick);
            dropped.Add(pick);
        }

        var tachyonsLost = (int)Math.Round(traveler.Tachyons.Current * TachyonPenaltyFraction);
        if (tachyonsLost > 0)
        {
            traveler.Tachyons.Spend(tachyonsLost);
        }

        traveler.SetCurrentYear(TimeScale.MinYear);
        traveler.PlaceAt(world.GetYear(TimeScale.MinYear).Map.Start);
        traveler.Health.Heal(traveler.Health.Max);

        return new Outcome(deathYear, deathPosition, dropped, tachyonsLost);
    }
}
