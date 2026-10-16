using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;

namespace OpenSkyAtc.Core.Planning;

/// <summary>A planned taxi route to a holding point.</summary>
/// <param name="HoldingPoint">The holding point, if the airport data has one for the runway.</param>
/// <param name="Target">Where the taxi ends; the holding point, or the runway end when none is known.</param>
/// <param name="Via">Taxiway names; empty for the no-route fallback.</param>
/// <param name="HoldShorts">Runways to hold short of on the way.</param>
/// <param name="Path">Route centreline; empty when no route is known.</param>
public sealed record TaxiPlan(
    HoldingPoint? HoldingPoint,
    GeoPoint Target,
    IReadOnlyList<string> Via,
    IReadOnlyList<string> HoldShorts,
    IReadOnlyList<GeoPoint> Path)
{
    public bool IsFallback => Via.Count == 0;
}

/// <summary>Plans taxi routes. Step 8 adds the taxiway-graph planner; until then <see cref="FallbackTaxiPlanner"/> is used.</summary>
public interface ITaxiPlanner
{
    TaxiPlan PlanToHoldingPoint(AirportData airport, GeoPoint from, RunwayEnd runway);
}

/// <summary>
/// The design doc's bad-data fallback: "taxi to holding point runway 24L" with no route. Uses the runway's
/// holding point closest to the runway end (full-length departure) when the airport data has one.
/// </summary>
public sealed class FallbackTaxiPlanner : ITaxiPlanner
{
    public TaxiPlan PlanToHoldingPoint(AirportData airport, GeoPoint from, RunwayEnd runway)
    {
        var hp = airport.HoldingPoints
            .Where(h => string.Equals(h.RunwayDesignator, runway.Designator, StringComparison.OrdinalIgnoreCase))
            .OrderBy(h => GeoMath.DistanceM(h.Position, runway.Position))
            .FirstOrDefault();
        return new TaxiPlan(hp, hp?.Position ?? runway.Position, [], [], []);
    }
}
