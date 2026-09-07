using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Monsters;
using ChronoTravelers.Engine.Content;
using ChronoTravelers.Engine.Simulation;

namespace ChronoTravelers.PlaytestHarness;

/// <summary>
/// All the per-bot state <see cref="PlaytestRunner.StepBot"/> carries
/// between ticks — the single-class runner keeps exactly one of these; the
/// simultaneous runner keeps one per class and steps them all against a
/// single shared <see cref="WorldSimulation.TickMultiplayer"/> call. Fields
/// mirror the locals the original inline RunLoop kept.
/// </summary>
public sealed class BotState
{
    public Traveler Bot { get; }
    public RunReport Report { get; }
    public List<AbilityData> ClassAbilities { get; }
    public double Aggression { get; }
    public bool VerboseFatal { get; }

    /// <summary>Host state threaded into <see cref="WorldSimulation.TickMultiplayer"/> — unused by the single-player path.</summary>
    public PlayerTickState TickState { get; }

    public int TicksSinceMonster;
    public Monster? ShadowTarget;
    public int ShadowTicks;

    public readonly HashSet<Monster> RangedGaveUpOn = new(ReferenceEqualityComparer.Instance);
    public Monster? RangedTarget;
    public int RangedChaseTicks;

    /// <summary>HP snapshot taken just before the shared tick, so ambush/tick damage can be attributed after it (see the single-player path's identical check).</summary>
    public int HpBeforeTick;

    /// <summary>Set once when this bot's death has been folded into <see cref="Report"/> so the simultaneous loop doesn't double-count it every later tick.</summary>
    public bool DeathRecorded;

    public BotState(CharacterClass characterClass, long worldSeed, IReadOnlyList<AbilityData> allAbilities, double aggression, bool verboseFatal)
    {
        Bot = new Traveler($"{characterClass}Bot", characterClass);
        Report = new RunReport { CharacterName = Bot.Name, WorldSeed = worldSeed };
        ClassAbilities = allAbilities
            .Where(a => string.Equals(a.Class, characterClass.ToString(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        Aggression = aggression;
        VerboseFatal = verboseFatal;
        TickState = new PlayerTickState { Player = Bot };
    }
}
