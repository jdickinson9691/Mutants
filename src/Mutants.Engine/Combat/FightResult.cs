using Mutants.Core.Items;

namespace Mutants.Engine.Combat;

/// <summary>Outcome of a single <see cref="CombatResolver.Fight"/> call.</summary>
public sealed record FightResult(
    bool MutantWon,
    int Rounds,
    int XpAwarded,
    IReadOnlyList<Item> ItemsDropped,
    IReadOnlyList<string> Log);
