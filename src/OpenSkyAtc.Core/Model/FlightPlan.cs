using OpenSkyAtc.Core.Geo;

namespace OpenSkyAtc.Core.Model;

public sealed record RouteWaypoint(string Ident, GeoPoint Position, string? Airway = null);

/// <summary>The filed flight plan, imported once from SimBrief or a local .pln file.</summary>
public sealed record FlightPlan
{
    public required string Callsign { get; init; }

    /// <summary>Spoken airline name ("Vueling"); null for registrations or unknown airlines.</summary>
    public string? Telephony { get; init; }

    public string? AircraftType { get; init; }
    public required string Origin { get; init; }
    public required string Destination { get; init; }
    public string? Alternate { get; init; }
    public IReadOnlyList<RouteWaypoint> Route { get; init; } = [];
    public AltitudeTarget? CruiseLevel { get; init; }
    public string? PlannedSid { get; init; }
    public string? PlannedStar { get; init; }
    public string? DepartureRunway { get; init; }
    public string? ArrivalRunway { get; init; }

    /// <summary>Arrival stand from the plan, used for taxi-in when available.</summary>
    public string? DestinationStand { get; init; }
}
