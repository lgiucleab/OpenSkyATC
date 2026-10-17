namespace OpenSkyAtc.SimBrief;

public enum FlightPlanImportError
{
    /// <summary>No SimBrief Pilot ID set, or it is not a number of up to 7 digits.</summary>
    MissingPilotId,

    /// <summary>SimBrief does not know that Pilot ID.</summary>
    UnknownPilot,

    /// <summary>The pilot has no flight plan on SimBrief (none generated, or it expired), or the plan lacks origin/destination.</summary>
    NoFlightPlan,

    /// <summary>SimBrief could not be reached (no connection, timeout, server error).</summary>
    Network,

    /// <summary>The response or file is not a flight plan this importer understands.</summary>
    BadResponse,
}

/// <summary>A flight plan import failed; <see cref="Exception.Message"/> is written for the user.</summary>
public sealed class FlightPlanImportException(FlightPlanImportError error, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public FlightPlanImportError Error { get; } = error;
}
