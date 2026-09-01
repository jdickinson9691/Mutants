namespace ChronoTravelers.Engine.Simulation;

/// <summary>Why a <see cref="TimeTravelResolver.Travel"/> attempt failed. Travel is otherwise unrestricted — no unlock, no Warden gate, no minimum character level.</summary>
public enum TimeTravelFailureReason
{
    /// <summary>The target year is outside the timeline (2000–5000).</summary>
    YearOutOfRange,

    /// <summary>Not enough Tachyons to pay <see cref="ChronoTravelers.Core.Tachyons.TachyonEconomy.TimeTravelCost"/> for this jump.</summary>
    InsufficientTachyons,
}
