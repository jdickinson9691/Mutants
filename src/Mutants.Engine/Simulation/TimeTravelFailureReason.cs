namespace Mutants.Engine.Simulation;

public enum TimeTravelFailureReason
{
    /// <summary>No level is defined with that number.</summary>
    UnknownLevel,

    /// <summary>The Mutant's character level is below that level's Mutants.Core.Levels.WorldLevelDefinition.MinCharacterLevelToUnlock.</summary>
    BelowMinimumCharacterLevel,

    /// <summary>The level's gatekeeper had to be fought to unlock it, and the Mutant lost.</summary>
    LostToGatekeeper,

    /// <summary>Not enough Ions to pay Mutants.Core.Ions.IonEconomy.TimeTravelCost for this jump.</summary>
    InsufficientIons,
}
