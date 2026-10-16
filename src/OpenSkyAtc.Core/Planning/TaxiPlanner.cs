using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Core.Planning;

/// <summary>A runway crossing on a taxi route: the runway (one end's designator) and the hold-short before it.</summary>
public sealed record TaxiCrossing(string Runway, GeoPoint HoldShort);

/// <summary>A planned taxi route.</summary>
/// <param name="HoldingPoint">The holding point (taxi-out), if known.</param>
/// <param name="Target">Where the taxi ends: the holding point, stand, or the runway end when nothing else is known.</param>
/// <param name="Via">Taxiway names; empty for the no-route fallback.</param>
/// <param name="HoldShorts">Runways to hold short of on the way, in order.</param>
/// <param name="Path">Route centreline; empty when no route is known.</param>
public sealed record TaxiPlan(
    HoldingPoint? HoldingPoint,
    GeoPoint Target,
    IReadOnlyList<string> Via,
    IReadOnlyList<string> HoldShorts,
    IReadOnlyList<GeoPoint> Path)
{
    public bool IsFallback => Via.Count == 0;

    public IReadOnlyList<TaxiCrossing> Crossings { get; init; } = [];

    /// <summary>Why no route was given (design doc: log the airport so a fixes list can be built); null for a routed plan.</summary>
    public string? FallbackReason { get; init; }
}

/// <summary>Plans taxi routes.</summary>
public interface ITaxiPlanner
{
    TaxiPlan PlanToHoldingPoint(AirportData airport, GeoPoint from, RunwayEnd runway);

    /// <summary>Picks the stand for taxi-in: the planned one when the airport has it, else the nearest suitable one.</summary>
    Stand? ChooseStand(AirportData airport, string? planned, GeoPoint from, EngineType engine);

    TaxiPlan PlanToStand(AirportData airport, GeoPoint from, Stand? stand);
}

/// <summary>
/// The design doc's bad-data fallback: "taxi to holding point runway 24L" with no route. Uses the runway's
/// holding point closest to the runway end (full-length departure) when the airport data has one.
/// </summary>
public sealed class FallbackTaxiPlanner : ITaxiPlanner
{
    public TaxiPlan PlanToHoldingPoint(AirportData airport, GeoPoint from, RunwayEnd runway) => Fallback(airport, runway, "no taxi graph");

    internal static TaxiPlan Fallback(AirportData airport, RunwayEnd runway, string reason)
    {
        var hp = airport.HoldingPoints
            .Where(h => string.Equals(h.RunwayDesignator, runway.Designator, StringComparison.OrdinalIgnoreCase))
            .OrderBy(h => GeoMath.DistanceM(h.Position, runway.Position))
            .FirstOrDefault();
        return new TaxiPlan(hp, hp?.Position ?? runway.Position, [], [], []) { FallbackReason = reason };
    }

    public Stand? ChooseStand(AirportData airport, string? planned, GeoPoint from, EngineType engine) =>
        GraphTaxiPlanner.PickStand(airport.Taxi?.Stands ?? [], planned, from, engine);

    public TaxiPlan PlanToStand(AirportData airport, GeoPoint from, Stand? stand) =>
        new(null, stand?.Position ?? from, [], [], []) { FallbackReason = "no taxi graph" };
}
