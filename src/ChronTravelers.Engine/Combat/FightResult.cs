using ChronTravelers.Core.Items;

namespace ChronTravelers.Engine.Combat;

/// <summary>Outcome of a single <see cref="CombatResolver.Fight"/> call.</summary>
public sealed record FightResult(
    bool TravelerWon,
    int Rounds,
    int XpAwarded,
    IReadOnlyList<Item> ItemsDropped,
    IReadOnlyList<string> Log);
