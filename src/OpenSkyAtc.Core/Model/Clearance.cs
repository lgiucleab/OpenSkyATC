using OpenSkyAtc.Core.Geo;

namespace OpenSkyAtc.Core.Model;

/// <summary>A taxi clearance: where to, via which taxiways, and every hold-short on the way.</summary>
/// <param name="Destination">Holding point name or stand.</param>
/// <param name="Runway">Departure runway for taxi-out; null for taxi-in.</param>
/// <param name="Via">Taxiway names in order; empty for the no-route fallback.</param>
/// <param name="HoldShorts">Runways to hold short of, in order.</param>
/// <param name="Path">Route centreline, for off-route checks and the in-sim arrows. Empty when no route is known.</param>
public sealed record TaxiClearance(
    string Destination,
    string? Runway,
    IReadOnlyList<string> Via,
    IReadOnlyList<string> HoldShorts,
    IReadOnlyList<GeoPoint> Path)
{
    /// <summary>Runway crossings on the route with the hold-short position before each (for crossing clearances and the arrows).</summary>
    public IReadOnlyList<(string Runway, GeoPoint HoldShort)> Crossings { get; init; } = [];
}

/// <summary>
/// The single source of truth for what ATC has cleared. Every ATC instruction updates it, and
/// conformance checks read only this record, never the phraseology text.
/// </summary>
public sealed class Clearance
{
    private readonly HashSet<string> _crossCleared = new(StringComparer.OrdinalIgnoreCase);

    public AltitudeTarget? ClearedAltitude { get; internal set; }

    /// <summary>Assigned heading in degrees magnetic; null means own navigation.</summary>
    public int? AssignedHeadingDeg { get; internal set; }

    public int? SpeedRestrictionKt { get; internal set; }
    public int? Squawk { get; internal set; }

    public string? Sid { get; internal set; }
    public string? Star { get; internal set; }
    public string? Approach { get; internal set; }

    /// <summary>Fix cleared direct to, if any.</summary>
    public string? DirectTo { get; internal set; }

    /// <summary>Runway in use for this flight's departure or arrival.</summary>
    public string? Runway { get; internal set; }

    public TaxiClearance? Taxi { get; internal set; }

    public int? CurrentFrequencyKhz { get; internal set; }

    /// <summary>Frequency the aircraft was handed off to and has not called on yet.</summary>
    public int? NextFrequencyKhz { get; internal set; }

    /// <summary>The unit that issued the pending handoff (it repeats it if the aircraft does not change frequency).</summary>
    public ControllerUnit? HandoffFrom { get; internal set; }

    /// <summary>The unit the pending handoff is to.</summary>
    public ControllerUnit? HandoffTo { get; internal set; }

    public string? LineUpCleared { get; internal set; }
    public string? TakeoffCleared { get; internal set; }
    public string? LandingCleared { get; internal set; }
    public IReadOnlySet<string> CrossCleared => _crossCleared;

    public bool IsLineUpCleared(Runway rwy) => LineUpCleared is { } d && rwy.Has(d);
    public bool IsTakeoffCleared(Runway rwy) => TakeoffCleared is { } d && rwy.Has(d);
    public bool IsLandingCleared(Runway rwy) => LandingCleared is { } d && rwy.Has(d);
    public bool IsCrossCleared(Runway rwy) => _crossCleared.Contains(rwy.First.Designator) || _crossCleared.Contains(rwy.Second.Designator);

    internal void SetHandoff(ControllerUnit from, ControllerUnit to)
    {
        HandoffFrom = from;
        HandoffTo = to;
        NextFrequencyKhz = to.FrequencyKhz;
    }

    internal void ClearHandoff()
    {
        HandoffFrom = HandoffTo = null;
        NextFrequencyKhz = null;
    }

    internal void ClearToCross(string runway) => _crossCleared.Add(runway);
    internal void RemoveCrossClearance(string runway) => _crossCleared.Remove(runway);
}
