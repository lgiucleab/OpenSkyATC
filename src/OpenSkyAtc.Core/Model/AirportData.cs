using OpenSkyAtc.Core.Geo;

namespace OpenSkyAtc.Core.Model;

/// <summary>One end of a runway: its designator ("24L"), position and true heading when departing from it.</summary>
/// <param name="ApproachType">Instrument approach to this end, e.g. "ILS". Null when the data has none; "ILS" is then assumed (step 6).</param>
/// <param name="ApproachAltitudeFt">Initial approach altitude from procedure data, when the sim has it.</param>
public sealed record RunwayEnd(string Designator, GeoPoint Position, double HeadingTrueDeg, string? ApproachType = null, int? ApproachAltitudeFt = null);

/// <summary>A physical runway between two ends. Its polygon is the rectangle between the ends, <see cref="WidthM"/> wide.</summary>
public sealed record Runway(RunwayEnd First, RunwayEnd Second, double WidthM)
{
    public string Name => $"{First.Designator}/{Second.Designator}";

    public double LengthM => GeoMath.DistanceM(First.Position, Second.Position);

    public bool Has(string designator) =>
        string.Equals(First.Designator, designator, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Second.Designator, designator, StringComparison.OrdinalIgnoreCase);

    public RunwayEnd? End(string designator) =>
        string.Equals(First.Designator, designator, StringComparison.OrdinalIgnoreCase) ? First
        : string.Equals(Second.Designator, designator, StringComparison.OrdinalIgnoreCase) ? Second
        : null;

    /// <summary>
    /// Position relative to the centreline in metres: along-track from <see cref="First"/> towards
    /// <see cref="Second"/>, and cross-track (positive to the right of that direction).
    /// </summary>
    public (double Along, double Cross) Project(GeoPoint p)
    {
        var frame = new LocalFrame(First.Position);
        var (ex, ny) = frame.ToLocal(Second.Position);
        double length = Math.Sqrt(ex * ex + ny * ny);
        double ux = ex / length, uy = ny / length;
        var (px, py) = frame.ToLocal(p);
        return (px * ux + py * uy, px * uy - py * ux);
    }
}

/// <summary>A runway hold-short point from the taxi graph. The line runs across the taxiway at this point.</summary>
public sealed record HoldShortPoint(GeoPoint Position, string RunwayDesignator);

/// <summary>A radio frequency published for the airport. <paramref name="Callsign"/> overrides the generated one ("Barcelona Ground").</summary>
public sealed record AirportFrequency(ControllerRole Role, int FrequencyKhz, string? Callsign = null);

/// <summary>A named runway holding point. <paramref name="Name"/> may be null when the data has no name.</summary>
public sealed record HoldingPoint(string? Name, GeoPoint Position, string RunwayDesignator);

/// <summary>
/// Airport data from SimConnect facilities (live) or JSON fixtures (tests).
/// Parking and the taxi graph are added in the steps that need them.
/// </summary>
public sealed record AirportData
{
    public required string Icao { get; init; }

    /// <summary>Name used in controller callsigns, e.g. "Barcelona". Falls back to the ICAO code.</summary>
    public string? Name { get; init; }
    public double? ElevationFt { get; init; }

    /// <summary>Airport reference point. When unknown, <see cref="ReferencePoint"/> falls back to the runway ends.</summary>
    public GeoPoint? Location { get; init; }
    public IReadOnlyList<Runway> Runways { get; init; } = [];
    public IReadOnlyList<HoldShortPoint> HoldShorts { get; init; } = [];
    public IReadOnlyList<HoldingPoint> HoldingPoints { get; init; } = [];
    public IReadOnlyList<AirportFrequency> Frequencies { get; init; } = [];

    /// <summary>From the configurable transition data file; null when unknown. Never invented.</summary>
    public int? TransitionAltitudeFt { get; init; }

    /// <summary>Flight level number (e.g. 70 for FL070); null when unknown.</summary>
    public int? TransitionLevel { get; init; }

    public Runway? FindRunway(string designator) => Runways.FirstOrDefault(r => r.Has(designator));

    /// <summary>The reference point, or the mean of the runway ends; null when the airport has neither.</summary>
    public GeoPoint? ReferencePoint =>
        Location ?? (Runways.Count == 0 ? null : new GeoPoint(
            Runways.Average(r => (r.First.Position.Latitude + r.Second.Position.Latitude) / 2),
            Runways.Average(r => (r.First.Position.Longitude + r.Second.Position.Longitude) / 2)));
}
