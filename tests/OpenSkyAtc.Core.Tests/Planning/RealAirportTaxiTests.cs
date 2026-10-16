using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Planning;
using OpenSkyAtc.Core.Sim;
using Xunit.Abstractions;

namespace OpenSkyAtc.Core.Tests.Planning;

/// <summary>
/// Taxi routing on airports exported from the owner's sim (tests/fixtures/airports/*.facility.json). These files are
/// the sim's data and are not in the repository, so the tests do nothing on machines that do not have them.
/// </summary>
public class RealAirportTaxiTests(ITestOutputHelper output)
{
    private static string? Dir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            string candidate = Path.Combine(d.FullName, "tests", "fixtures", "airports");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static IEnumerable<(string Icao, AirportData Airport)> Airports()
    {
        if (Dir() is not { } dir) yield break;
        foreach (var f in Directory.EnumerateFiles(dir, "*.facility.json").OrderBy(f => f))
        {
            var dump = FacilityDump.Parse(File.ReadAllText(f));
            yield return (dump.Icao, FacilityConverter.ToAirportData(dump));
        }
    }

    [Fact]
    public void EveryRunwayEnd_GetsARouteOrTheFallback_FromAGate()
    {
        var planner = new GraphTaxiPlanner();
        foreach (var (icao, airport) in Airports())
        {
            var graph = airport.Taxi!;
            var gate = graph.Stands.FirstOrDefault(s => s.Gate) ?? graph.Stands.FirstOrDefault();
            output.WriteLine($"== {icao} {airport.Name}: {airport.Runways.Count} runways, {graph.Nodes.Count} nodes, {graph.Edges.Count} edges, {graph.Stands.Count} stands, TA {airport.TransitionAltitudeFt}");
            if (gate is null) continue;
            foreach (var end in airport.Runways.SelectMany(r => new[] { r.First, r.Second }))
            {
                var plan = planner.PlanToHoldingPoint(airport, gate.Position, end);
                output.WriteLine($"   {gate.Name} -> {end.Designator}: " + (plan.IsFallback
                    ? $"FALLBACK ({plan.FallbackReason})"
                    : $"via {string.Join(", ", plan.Via)}{string.Concat(plan.HoldShorts.Select(h => $", hold short {h}"))} ({plan.Path.Count} points)"));

                if (!plan.IsFallback)
                {
                    Assert.True(plan.Path.Count >= 2);
                    Assert.True(GeoMath.DistanceM(plan.Path[^1], plan.Target) < 1, "the route ends at the holding point");
                    Assert.True(GeoMath.DistanceM(plan.Path[0], gate.Position) < 50, "the route starts at the gate");
                }
            }
        }
    }

    [Theory]
    [InlineData("LECO")]   // every taxiway unnamed
    [InlineData("LEVD")]   // no taxi network at all
    public void KnownBadAirports_FallBack(string icao)
    {
        var found = Airports().FirstOrDefault(a => a.Icao == icao);
        if (found.Airport is null) return; // fixture not on this machine
        var planner = new GraphTaxiPlanner();
        var from = found.Airport.Taxi!.Stands.FirstOrDefault()?.Position ?? found.Airport.ReferencePoint ?? new GeoPoint(0, 0);
        foreach (var end in found.Airport.Runways.SelectMany(r => new[] { r.First, r.Second }))
        {
            var plan = planner.PlanToHoldingPoint(found.Airport, from, end);
            output.WriteLine($"{icao} -> {end.Designator}: {plan.FallbackReason}");
            Assert.True(plan.IsFallback);
        }
    }

    [Fact]
    public void TaxiIn_FromARunwayExit_ToTheNearestGate()
    {
        var planner = new GraphTaxiPlanner();
        foreach (var (icao, airport) in Airports())
        {
            var graph = airport.Taxi!;
            if (graph.Stands.Count == 0 || airport.Runways.Count == 0) continue;
            var hold = graph.Nodes.FirstOrDefault(n => n.Kind == TaxiNodeKind.HoldShort);
            if (hold is null) continue;
            var stand = planner.ChooseStand(airport, null, hold.Position, EngineType.Jet);
            var plan = planner.PlanToStand(airport, hold.Position, stand);
            output.WriteLine($"{icao}: to stand {stand?.Name}: " + (plan.IsFallback ? $"FALLBACK ({plan.FallbackReason})" : $"via {string.Join(", ", plan.Via)}"));
        }
    }
}
