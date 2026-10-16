using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;

namespace OpenSkyAtc.Core.Airspace;

/// <summary>Where the host looks for CENTER frequencies before a flight, and how they are combined.</summary>
public static class CenterSearch
{
    /// <summary>Points along the planned route (origin, waypoints, destination) at most <paramref name="stepNm"/> apart.</summary>
    public static IReadOnlyList<GeoPoint> RoutePoints(AirportData origin, AirportData destination, FlightPlan plan, double stepNm = 50)
    {
        if (origin.ReferencePoint is null && destination.ReferencePoint is null && plan.Route.Count == 0) return [];
        var line = RouteLine.From(origin, destination, plan);
        var points = new List<GeoPoint>();
        for (double d = 0; d < line.LengthNm; d += stepNm) points.Add(line.PointAt(d));
        points.Add(line.Destination);
        return points;
    }

    /// <summary>The sim's CENTER frequencies plus the owner's file; the same frequency within 1 nm counts once.</summary>
    public static IReadOnlyList<CenterFrequency> Merge(IEnumerable<CenterFrequency> fromSim, IEnumerable<CenterFrequency> fromFile)
    {
        var result = new List<CenterFrequency>();
        foreach (var c in fromFile.Concat(fromSim))
            if (!result.Any(r => r.FrequencyKhz == c.FrequencyKhz && GeoMath.DistanceNm(r.Position, c.Position) < 1)) result.Add(c);
        return result;
    }
}
