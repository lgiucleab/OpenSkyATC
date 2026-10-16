using OpenSkyAtc.Core.Geo;

namespace OpenSkyAtc.Core.Model;

/// <summary>One end of a runway: its designator ("24L"), position and true heading when departing from it.</summary>
public sealed record RunwayEnd(string Designator, GeoPoint Position, double HeadingTrueDeg);

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

/// <summary>
/// Airport data from SimConnect facilities (live) or JSON fixtures (tests).
/// Frequencies, parking and the taxi graph are added in the steps that need them.
/// </summary>
public sealed record AirportData
{
    public required string Icao { get; init; }
    public double? ElevationFt { get; init; }
    public IReadOnlyList<Runway> Runways { get; init; } = [];
    public IReadOnlyList<HoldShortPoint> HoldShorts { get; init; } = [];

    /// <summary>From the configurable transition data file; null when unknown. Never invented.</summary>
    public int? TransitionAltitudeFt { get; init; }

    /// <summary>Flight level number (e.g. 70 for FL070); null when unknown.</summary>
    public int? TransitionLevel { get; init; }

    public Runway? FindRunway(string designator) => Runways.FirstOrDefault(r => r.Has(designator));
}
