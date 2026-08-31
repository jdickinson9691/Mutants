using Mutants.Core.Items;

namespace Mutants.Core.Characters;

/// <summary>
/// A temporary stat buff currently affecting a Mutant, from a consumed
/// potion (see <see cref="Mutant.Consume"/>) — ticks down once per world
/// tick (<see cref="Mutant.AdvanceEffectTicks"/>) until it expires. Only
/// the temporary effect types (BuffAttack/BuffDefense) ever end up here;
/// Heal is instant and never creates one.
/// </summary>
public sealed record ActiveEffect(ConsumableEffectType Type, double Magnitude, int TicksRemaining);
