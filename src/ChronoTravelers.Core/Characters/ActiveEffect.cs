using ChronoTravelers.Core.Items;

namespace ChronoTravelers.Core.Characters;

/// <summary>
/// A timed effect currently affecting a Traveler, from a consumed potion
/// (see <see cref="Traveler.Consume"/>) — ticks down once per world tick
/// (<see cref="Traveler.AdvanceEffectTicks"/>) until it expires. Only the
/// timed effect types (BuffAttack/BuffDefense/BuffSpeed/HealOverTime) ever
/// end up here; Heal and RestoreTachyons are instant and never create one.
/// HealOverTime is the one type here that <em>does</em> something on every
/// tick (heals) rather than just expiring — see AdvanceEffectTicks.
/// </summary>
public sealed record ActiveEffect(ConsumableEffectType Type, double Magnitude, int TicksRemaining);
