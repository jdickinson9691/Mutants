using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;

namespace ChronoTravelers.Core.Diagnostics;

/// <summary>
/// Opt-in observation hook for when an always-on <see cref="PassiveTrait"/>
/// actually had a nonzero effect on something, rather than just being
/// unlocked — for a class-tuning playtest harness to report real per-run
/// activation counts/magnitudes instead of a static list of what's
/// unlocked at a given level. Silent no-op unless a listener is attached
/// (<see cref="Listener"/>); no production code path ever sets one, so
/// this costs one null check and a branch at each call site.
/// </summary>
public static class PassiveActivationTracker
{
    /// <summary>
    /// Fired by <see cref="Record"/>. <c>magnitude</c> is the concrete
    /// unit of impact for that hook where one exists (HP saved/restored,
    /// Credits or Tachyons gained, attack points added) — a raw fraction
    /// only for hooks with no natural unit (e.g. a dodge/negate roll,
    /// recorded as 1.0 per activation).
    /// </summary>
    public static Action<CharacterClass, PassiveHook, double>? Listener;

    /// <summary>Reports one activation. A no-op if <paramref name="magnitude"/> is zero (nothing actually happened) or no listener is attached.</summary>
    public static void Record(CharacterClass cls, PassiveHook hook, double magnitude)
    {
        if (magnitude != 0)
        {
            Listener?.Invoke(cls, hook, magnitude);
        }
    }
}
