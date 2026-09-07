using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Diagnostics;
using ChronoTravelers.Core.World;
using ChronoTravelers.Engine.Content;

namespace ChronoTravelers.Engine.Combat;

/// <summary>
/// Casts an ability outside a fight — docs/GDD.md item #5's gap-analysis
/// finding: three abilities (Doctor "Crash Cart", Spy "Black Market
/// Contacts", Engineer "Jump Rig") shipped with <c>Effect: "None"</c>
/// because their described effects are a party/economy/overworld mechanic
/// this engine didn't model, so <see cref="CombatSession.Cast"/> — built
/// for the round-by-round fight loop — could never do anything with them.
/// Black Market Contacts turned out to need no cast at all (it's
/// "Permanent" — see <see cref="Traveler.StoreDiscountBonus"/>, which folds
/// its bonus in automatically once unlocked); this resolver covers the
/// other two, which are genuinely on-demand.
///
/// Deliberately separate from <see cref="CombatSession"/> rather than
/// folded into it: these effects have nothing to do with the round loop
/// (no opponent, no attack/defense roll), and every caller of this class
/// — the console's top-level command dispatch, the multiplayer server's
/// <c>Commands.cs</c> — already has to check "am I in a fight?" before
/// routing to one or the other, so keeping them as two small, focused
/// entry points is simpler than one method branching on context.
/// </summary>
public static class OverworldAbilityResolver
{
    /// <summary>
    /// A wounded NPC counts as "downed" enough for <see cref="ReviveAlly"/>
    /// to target — see that method's doc comment for why this reads
    /// "downed" as "badly wounded" rather than literally 0 HP.
    /// </summary>
    public const double DownedHpFraction = 0.25;

    public readonly record struct Result(bool Success, string Message, Traveler? RevivedAlly = null);

    /// <summary>
    /// Validates that <paramref name="caster"/> can cast <paramref name="ability"/>
    /// right now (class, level unlock, an overworld — not combat — effect
    /// type, and enough Tachyons) and, if so, spends the cost (applying
    /// Scientist "Stable Core"'s free-cast roll same as
    /// <see cref="CombatSession.Cast"/> does) — shared setup for
    /// <see cref="TryShortTeleport"/>/<see cref="TryReviveAlly"/> so the two
    /// don't duplicate it.
    /// </summary>
    private static bool TryValidateAndSpend(Traveler caster, AbilityData ability, AbilityEffectType expectedEffect, IRandomSource random, out string? failureMessage)
    {
        if (!Enum.TryParse<CharacterClass>(ability.Class, ignoreCase: true, out var abilityClass) || abilityClass != caster.Class)
        {
            failureMessage = $"{caster.Name} can't use {ability.Name} — that's a {ability.Class} ability.";
            return false;
        }

        if (caster.Level < ability.Level)
        {
            failureMessage = $"{ability.Name} unlocks at level {ability.Level} (you're level {caster.Level}).";
            return false;
        }

        if (!Enum.TryParse<AbilityEffectType>(ability.Effect, ignoreCase: true, out var effectType) || effectType != expectedEffect)
        {
            failureMessage = effectType is AbilityEffectType.None
                ? $"{ability.Name} has no effect yet — it's a passive/party/overworld mechanic this engine doesn't model."
                : $"{ability.Name} only works in a fight — try it with 'cast {ability.Name}' once you've engaged something.";
            return false;
        }

        // Scientist "Stable Core" — same free-cast roll CombatSession.Cast
        // applies mid-fight, kept consistent for a Scientist who happens
        // to also carry one of these two overworld abilities.
        var freeCast = caster.FreeCastChance > 0 && random.NextDouble() < caster.FreeCastChance;
        if (freeCast)
        {
            PassiveActivationTracker.Record(caster.Class, PassiveHook.FreeCastChancePct, ability.TachyonCost > 0 ? ability.TachyonCost : 1);
        }

        var cost = freeCast ? 0 : caster.EffectiveCastCost(ability.TachyonCost);

        if (!caster.Tachyons.CanAfford(cost))
        {
            failureMessage = $"Not enough Tachyons ({cost} needed; you have {caster.Tachyons.Current}).";
            return false;
        }

        if (cost > 0)
        {
            caster.Tachyons.Spend(cost);
        }

        failureMessage = null;
        return true;
    }

    /// <summary>
    /// Engineer "Jump Rig" (<see cref="AbilityEffectType.ShortTeleport"/>):
    /// teleports <paramref name="caster"/> to a random room within
    /// <c>Magnitude</c> exit-hops of their current position on
    /// <paramref name="map"/> (<see cref="LevelMap.RoomsWithinHops"/>) — a
    /// real jump, not a walk: it lands regardless of monsters/locked-off
    /// areas along the way, same as any other instant reposition in this
    /// engine (compare <see cref="Traveler.SetCurrentYear"/>'s year jump).
    /// Fails harmlessly (Tachyons already spent by <see cref="TryValidateAndSpend"/>
    /// stay spent — same as a combat ability that fizzles on a condition
    /// miss, see <see cref="CombatSession.PerformTravelerAttack"/>'s doc
    /// comment) if there's nowhere in range, which only happens on a
    /// single-room map.
    /// </summary>
    public static Result TryShortTeleport(Traveler caster, LevelMap map, AbilityData ability, IRandomSource random)
    {
        if (!TryValidateAndSpend(caster, ability, AbilityEffectType.ShortTeleport, random, out var failure))
        {
            return new Result(false, failure!);
        }

        var maxHops = Math.Max(1, (int)Math.Round(ability.Magnitude));
        var destinations = map.RoomsWithinHops(caster.Position, maxHops);
        if (destinations.Count == 0)
        {
            return new Result(false, $"{caster.Name} charges the rig, but there's nowhere nearby to jump to.");
        }

        var chosen = destinations[(int)(random.NextDouble() * destinations.Count)];
        caster.PlaceAt(chosen);
        return new Result(true, $"{caster.Name} flickers and reappears a short jump away.");
    }

    /// <summary>
    /// Doctor "Crash Cart" (<see cref="AbilityEffectType.ReviveAlly"/>):
    /// heals the most-wounded living NPC Traveler sharing <paramref name="caster"/>'s
    /// room (searched from <paramref name="npcsInRoom"/>, already filtered
    /// by the caller to living/same-year/same-position — see the console's
    /// <c>HandlePvpFight</c> targeting or ChronoTravelers.Game.Commands.Fight's
    /// identical NPC lookup for the same filter) up to <c>Magnitude</c> of
    /// their max HP, if that's more than they already have.
    ///
    /// docs/GDD.md's Doctor tree describes this as reviving a "downed"
    /// ally — read here as badly wounded (<see cref="DownedHpFraction"/> of
    /// max HP or less), not literally 0 HP. A genuinely dead NPC is
    /// replaced wholesale by the very next world tick
    /// (<see cref="Simulation.WorldSimulation.RespawnDeadNpcs"/> — fired
    /// every ~2 seconds on the multiplayer server, every loop iteration in
    /// single-player), which is nowhere near enough of a window for a
    /// player to notice a kill and react with a cast before the "ally" is
    /// simply a different NPC. Targeting the room's most-wounded-but-still-
    /// standing NPC instead is the interpretation that's actually usable in
    /// play — a documented scope call, not the letter of the flavor text.
    /// </summary>
    public static Result TryReviveAlly(Traveler caster, IReadOnlyList<Traveler> npcsInRoom, AbilityData ability, IRandomSource random)
    {
        // Find a valid target BEFORE charging anything — unlike a combat
        // cast (or Jump Rig's single-room edge), missing here means the
        // player pressed the button with nobody hurt, so it costs no
        // Tachyons (TryReviveAlly_NoWoundedNpcInRoom_FailsWithoutSpendingTachyons).
        var target = npcsInRoom
            .Where(n => !n.Health.IsDead && n.Health.Current <= n.Health.Max * DownedHpFraction)
            .OrderBy(n => n.Health.Current)
            .FirstOrDefault();

        if (target is null)
        {
            return new Result(false, $"{caster.Name} readies the crash cart, but no one here needs it.");
        }

        var targetAmount = (int)Math.Round(target.Health.Max * ability.Magnitude);
        if (targetAmount <= target.Health.Current)
        {
            return new Result(false, $"{caster.Name} readies the crash cart, but no one here needs it.");
        }

        if (!TryValidateAndSpend(caster, ability, AbilityEffectType.ReviveAlly, random, out var failure))
        {
            return new Result(false, failure!);
        }

        var healed = target.Health.Heal(targetAmount - target.Health.Current);
        return new Result(true, $"{caster.Name} works the crash cart on {target.Name} — {healed} HP restored.", target);
    }
}
