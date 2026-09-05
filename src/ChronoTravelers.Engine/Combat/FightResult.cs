using ChronoTravelers.Core.Items;

namespace ChronoTravelers.Engine.Combat;

/// <summary>Outcome of a single <see cref="CombatResolver.Fight"/> call.</summary>
public sealed record FightResult(
    bool TravelerWon,
    int Rounds,
    int XpAwarded,
    int CreditsAwarded,
    IReadOnlyList<Item> ItemsDropped,
    IReadOnlyList<string> Log);
