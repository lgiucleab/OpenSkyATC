using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Core.Planning;

public sealed record TaxiRoutingOptions
{
    public double NameChangePenaltyM { get; init; } = 400;        // design doc
    public double RunwayCrossingPenaltyM { get; init; } = 1500;   // design doc
    public double AlongOtherRunwayPenaltyM { get; init; } = 10_000; // design doc, per runway segment
    public double DropUnnamedShorterThanM { get; init; } = 60;    // design doc
    public double MaxUnnamedShare { get; init; } = 0.4;           // design doc

    /// <summary>The aircraft must be within this distance of the network to start a route.</summary>
    public double MaxStartDistanceM { get; init; } = 300;

    /// <summary>Apron lanes cost this many times their length, so routes use them only to reach stands.</summary>
    public double ApronLaneCostFactor { get; init; } = 3;

    /// <summary>Holding points compared (those nearest the runway end); the cheapest route wins.</summary>
    public int HoldingPointCandidates { get; init; } = 4;

    /// <summary>
    /// Added per metre a holding point is from the runway end, so of two about equally good routes the one to the
    /// full-length holding point wins; small next to a runway crossing, so it never sends a route across a runway.
    /// </summary>
    public double HoldingPointDistanceWeight { get; init; } = 1;

    /// <summary>A node this far beyond a runway end, within the runway's width, still counts as on the runway.</summary>
    public double RunwayEndMarginM { get; init; } = 60;
}

/// <summary>
/// Taxi routes on the airport's taxi graph (design doc "Taxi routing"): A* with length, +400 m per taxiway name
/// change, +1,500 m per runway crossing, +10,000 m per segment along another runway, parking lanes only at the
/// start or goal; then compressed to spoken instructions with a hold-short for every crossing. Falls back to
/// "taxi to holding point runway X" with no route when there is no path or more than 40 % of it is unnamed.
/// </summary>
public sealed class GraphTaxiPlanner(TaxiRoutingOptions? options = null) : ITaxiPlanner
{
    private readonly TaxiRoutingOptions _o = options ?? new TaxiRoutingOptions();

    public TaxiPlan PlanToHoldingPoint(AirportData airport, GeoPoint from, RunwayEnd runway)
    {
        if (airport.Taxi is not { } graph) return FallbackTaxiPlanner.Fallback(airport, runway, "no taxi graph");
        var departure = airport.FindRunway(runway.Designator);
        var start = StartNode(graph, from);
        if (start is null) return FallbackTaxiPlanner.Fallback(airport, runway, "aircraft not near the taxi network");

        var goals = graph.Nodes
            .Where(n => n.Kind == TaxiNodeKind.HoldShort && n.RunwayDesignator is { } d && departure?.Has(d) == true)
            .OrderBy(n => GeoMath.DistanceM(n.Position, runway.Position))
            .Take(_o.HoldingPointCandidates)
            .ToList();
        if (goals.Count == 0) return FallbackTaxiPlanner.Fallback(airport, runway, $"no hold-short point for runway {runway.Designator}");

        // Every candidate is routed; the cheapest wins (not simply the first reachable one, which could be on the far
        // side of the departure runway).
        var best = goals
            .Select(g => (Goal: g, Route: AStar(graph, airport, start.Id, g.Id, departure)))
            .Where(x => x.Route is not null)
            .OrderBy(x => x.Route!.Value.Cost + _o.HoldingPointDistanceWeight * GeoMath.DistanceM(x.Goal.Position, runway.Position))
            .Take(1);
        foreach (var (goal, found) in best)
        {
            var route = found!.Value.Edges;
            var plan = Describe(graph, airport, route, departure);
            var hp = new HoldingPoint(null, goal.Position, runway.Designator);
            if (plan.Via.Count == 0) return FallbackTaxiPlanner.Fallback(airport, runway, "no named taxiway on the route") with { HoldingPoint = hp, Target = goal.Position };
            if (plan.UnnamedShare > _o.MaxUnnamedShare)
                return FallbackTaxiPlanner.Fallback(airport, runway, $"{plan.UnnamedShare:P0} of the route is unnamed") with { HoldingPoint = hp, Target = goal.Position };
            return new TaxiPlan(hp, goal.Position, plan.Via, plan.HoldShorts, FromHere(from, plan.Path)) { Crossings = plan.Crossings };
        }
        return FallbackTaxiPlanner.Fallback(airport, runway, "no path to the holding point");
    }

    public Stand? ChooseStand(AirportData airport, string? planned, GeoPoint from, EngineType engine) =>
        PickStand(airport.Taxi?.Stands ?? [], planned, from, engine);

    public TaxiPlan PlanToStand(AirportData airport, GeoPoint from, Stand? stand)
    {
        TaxiPlan Fallback(string why) => new(null, stand?.Position ?? from, [], [], []) { FallbackReason = why };
        if (stand is null) return Fallback("no stand");
        if (airport.Taxi is not { } graph) return Fallback("no taxi graph");
        var start = StartNode(graph, from);
        if (start is null) return Fallback("aircraft not near the taxi network");

        if (AStar(graph, airport, start.Id, stand.NodeId, departure: null) is not { Edges: var route }) return Fallback("no path to the stand");
        var plan = Describe(graph, airport, route, departure: null);
        if (plan.UnnamedShare > _o.MaxUnnamedShare) return Fallback($"{plan.UnnamedShare:P0} of the route is unnamed");
        if (plan.Via.Count == 0) return Fallback("no named taxiway on the route");
        return new TaxiPlan(null, stand.Position, plan.Via, plan.HoldShorts, FromHere(from, plan.Path)) { Crossings = plan.Crossings };
    }

    /// <summary>The path starts where the aircraft is, so the leg to the first node is not "off route".</summary>
    private static IReadOnlyList<GeoPoint> FromHere(GeoPoint from, IReadOnlyList<GeoPoint> path) =>
        path.Count > 0 && GeoMath.DistanceM(from, path[0]) > 1 ? [from, .. path] : path;

    /// <summary>The planned stand by name; otherwise the nearest gate for jets (or ramp for others) with room for the aircraft.</summary>
    public static Stand? PickStand(IReadOnlyList<Stand> stands, string? planned, GeoPoint from, EngineType engine)
    {
        if (stands.Count == 0) return null;
        if (planned is not null && stands.FirstOrDefault(s => Clean(s.Name) == Clean(planned)) is { } named) return named;

        bool jet = engine == EngineType.Jet;
        double minRadius = jet ? 18 : 0;
        return stands.Where(s => s.Gate == jet && s.RadiusM >= minRadius).OrderBy(s => GeoMath.DistanceM(s.Position, from)).FirstOrDefault()
               ?? stands.OrderBy(s => GeoMath.DistanceM(s.Position, from)).First();

        static string Clean(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant()
            .Replace("GATE", "").Replace("STAND", "");
    }

    /// <summary>At a stand: its node; otherwise the nearest taxiway node.</summary>
    private TaxiNode? StartNode(TaxiGraph graph, GeoPoint from)
    {
        var stand = graph.Stands.FirstOrDefault(s => GeoMath.DistanceM(s.Position, from) <= Math.Max(s.RadiusM, 15));
        if (stand is not null) return graph.Nodes[stand.NodeId];
        return graph.Nearest(from, _o.MaxStartDistanceM, n => n.Kind != TaxiNodeKind.Parking);
    }

    private readonly record struct State(int Node, string? LastName);

    /// <summary>
    /// A* over (node, last taxiway name), so name changes can be costed. Entering any runway costs a crossing, the
    /// departure runway included: a taxi-out ends at its hold-short, and only Tower clears onto it.
    /// </summary>
    private (List<TaxiEdge> Edges, double Cost)? AStar(TaxiGraph graph, AirportData airport, int start, int goal, Runway? departure)
    {
        var inRunway = RunwayMembership(graph, airport, _o.RunwayEndMarginM);
        var goalPos = graph.Nodes[goal].Position;

        var open = new PriorityQueue<State, double>();
        var cost = new Dictionary<State, double>();
        var came = new Dictionary<State, (State Prev, TaxiEdge Edge)>();
        var first = new State(start, null);
        cost[first] = 0;
        open.Enqueue(first, GeoMath.DistanceM(graph.Nodes[start].Position, goalPos));

        while (open.TryDequeue(out var s, out _))
        {
            if (s.Node == goal) return (Rebuild(came, s), cost[s]);
            double here = cost[s];
            foreach (var e in graph.From(s.Node))
            {
                if (e.Kind == TaxiEdgeKind.Parking && e.From != start && e.To != goal && e.From != goal && e.To != start) continue;
                double c = e.Kind == TaxiEdgeKind.Apron ? e.LengthM * _o.ApronLaneCostFactor : e.LengthM;
                string? last = s.LastName;
                if (e.Kind == TaxiEdgeKind.Taxiway && e.Name is { } name)
                {
                    if (last is not null && last != name) c += _o.NameChangePenaltyM;
                    last = name;
                }
                int fromR = inRunway[e.From], toR = inRunway[e.To];
                if (toR >= 0 && toR != fromR) c += _o.RunwayCrossingPenaltyM;
                if (e.Kind == TaxiEdgeKind.Runway) c += _o.AlongOtherRunwayPenaltyM;

                var next = new State(e.To, last);
                double total = here + c;
                if (cost.TryGetValue(next, out double known) && known <= total) continue;
                cost[next] = total;
                came[next] = (s, e);
                open.Enqueue(next, total + GeoMath.DistanceM(graph.Nodes[e.To].Position, goalPos));
            }
        }
        return null;
    }

    private static List<TaxiEdge> Rebuild(Dictionary<State, (State Prev, TaxiEdge Edge)> came, State end)
    {
        var edges = new List<TaxiEdge>();
        for (var s = end; came.TryGetValue(s, out var step); s = step.Prev) edges.Add(step.Edge);
        edges.Reverse();
        return edges;
    }

    /// <summary>Index of the runway whose area (its width, from <paramref name="endMarginM"/> before one end to as far past the other) contains each node, or -1.</summary>
    private static int[] RunwayMembership(TaxiGraph graph, AirportData airport, double endMarginM)
    {
        var result = new int[graph.Nodes.Count];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = -1;
            var p = graph.Nodes[i].Position;
            for (int r = 0; r < airport.Runways.Count; r++)
            {
                var rwy = airport.Runways[r];
                var (along, cross) = rwy.Project(p);
                if (along >= -endMarginM && along <= rwy.LengthM + endMarginM && Math.Abs(cross) <= rwy.WidthM / 2)
                {
                    result[i] = r;
                    break;
                }
            }
        }
        return result;
    }

    private sealed record Description(IReadOnlyList<string> Via, IReadOnlyList<string> HoldShorts, IReadOnlyList<GeoPoint> Path,
        IReadOnlyList<TaxiCrossing> Crossings, double UnnamedShare);

    /// <summary>
    /// Instruction compression (design doc): consecutive edges with the same name collapse; unnamed connectors under
    /// 60 m are dropped; a hold-short for every runway crossed (other than the departure runway).
    /// </summary>
    private Description Describe(TaxiGraph graph, AirportData airport, List<TaxiEdge> route, Runway? departure)
    {
        var inRunway = RunwayMembership(graph, airport, _o.RunwayEndMarginM);
        var path = new List<GeoPoint> { graph.Nodes[route[0].From].Position };
        var via = new List<string>();
        var crossings = new List<TaxiCrossing>();
        double taxiLength = 0, unnamedLength = 0, unnamedRun = 0;
        GeoPoint lastHold = graph.Nodes[route[0].From].Position;

        foreach (var e in route)
        {
            path.Add(graph.Nodes[e.To].Position);
            if (graph.Nodes[e.From].Kind == TaxiNodeKind.HoldShort) lastHold = graph.Nodes[e.From].Position;

            int fromR = inRunway[e.From], toR = inRunway[e.To];
            if (toR >= 0 && toR != fromR && (departure is null || airport.Runways[toR] != departure))
            {
                var rwy = airport.Runways[toR];
                var entry = graph.Nodes[e.To].Position;
                string end = GeoMath.DistanceM(entry, rwy.First.Position) <= GeoMath.DistanceM(entry, rwy.Second.Position) ? rwy.First.Designator : rwy.Second.Designator;
                if (crossings.Count == 0 || crossings[^1].Runway != end) crossings.Add(new TaxiCrossing(end, lastHold));
            }

            if (e.Kind != TaxiEdgeKind.Taxiway) continue;
            taxiLength += e.LengthM;
            if (e.Name is null)
            {
                unnamedRun += e.LengthM;
                continue;
            }
            // An unnamed gap counts against the route only when it is long, is not just the lane out of the
            // stand (before the first named taxiway), and does not sit between two pieces of the same taxiway.
            bool bridged = via.Count > 0 && via[^1] == e.Name;
            if (unnamedRun >= _o.DropUnnamedShorterThanM && via.Count > 0 && !bridged) unnamedLength += unnamedRun;
            unnamedRun = 0;
            if (via.Count == 0 || via[^1] != e.Name) via.Add(e.Name);
        }
        if (unnamedRun >= _o.DropUnnamedShorterThanM && departure is not null) unnamedLength += unnamedRun; // the end of a taxi-in is the stand lane

        double share = taxiLength > 0 ? unnamedLength / taxiLength : 1;
        return new Description(via, crossings.Select(c => c.Runway).ToList(), path, crossings, share);
    }
}
