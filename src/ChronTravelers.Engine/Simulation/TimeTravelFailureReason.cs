namespace ChronTravelers.Engine.Simulation;

/// <summary>Why a <see cref="TimeTravelResolver.Travel"/> attempt failed. Travel is otherwise unrestricted — no unlock, no Gatekeeper gate, no minimum character level.</summary>
public enum TimeTravelFailureReason
{
    /// <summary>The target year is outside the timeline (2000–5000).</summary>
    YearOutOfRange,

    /// <summary>Not enough Ions to pay <see cref="ChronTravelers.Core.Ions.IonEconomy.TimeTravelCost"/> for this jump.</summary>
    InsufficientIons,
}
