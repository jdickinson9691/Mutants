using ChronTravelers.Engine.Combat;

namespace ChronTravelers.Engine.Npc;

/// <summary>What one NPC did on one tick — see <see cref="NpcController.Act"/>.</summary>
public sealed record NpcTickResult(
    string NpcName,
    NpcGoal Goal,
    string? MonsterName = null,
    FightResult? Fight = null,
    string? Detail = null);
