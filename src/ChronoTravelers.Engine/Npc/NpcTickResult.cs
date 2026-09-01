using ChronoTravelers.Engine.Combat;

namespace ChronoTravelers.Engine.Npc;

/// <summary>What one NPC did on one tick — see <see cref="NpcController.Act"/>.</summary>
public sealed record NpcTickResult(
    string NpcName,
    NpcGoal Goal,
    string? MonsterName = null,
    FightResult? Fight = null,
    string? Detail = null);
