using ChronoTravelers.Core.Items;

namespace ChronoTravelers.Engine.Combat;

/// <summary>Outcome of a single <see cref="CombatResolver.FightTraveler"/> call — docs/GDD.md §11's "PvP via NPC-Traveler combat."</summary>
public sealed record TravelerFightResult(
    bool AttackerWon,
    int Rounds,
    int XpAwarded,
    int CreditsAwarded,
    IReadOnlyList<Item> ItemsDropped,
    IReadOnlyList<string> Log);
