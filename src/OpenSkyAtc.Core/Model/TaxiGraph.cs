using OpenSkyAtc.Core.Geo;

namespace OpenSkyAtc.Core.Model;

public enum TaxiNodeKind
{
    Normal,
    HoldShort,
    Parking,
}

public enum TaxiEdgeKind
{
    Taxiway,
    Runway,
    Parking,

    /// <summary>Unnamed apron lane (sim path type 6 "vehicle"): at some airports the only link from stands to taxiways.</summary>
    Apron,
}

/// <param name="RunwayDesignator">For hold-short nodes: the runway they protect (one end's designator), when known.</param>
public sealed record TaxiNode(int Id, GeoPoint Position, TaxiNodeKind Kind, string? RunwayDesignator = null);

/// <param name="Name">Taxiway name; null when unnamed.</param>
/// <param name="Runway">For runway edges: the physical runway's name ("14R/32L").</param>
public sealed record TaxiEdge(int From, int To, string? Name, double LengthM, TaxiEdgeKind Kind, string? Runway = null);

/// <summary>A parking spot (stand) and its graph node.</summary>
/// <param name="Name">Display name, e.g. "T527" or "C25".</param>
/// <param name="Gate">True for gates (jet stands), false for ramps.</param>
public sealed record Stand(string Name, GeoPoint Position, double HeadingTrueDeg, double RadiusM, bool Gate, int NodeId);

/// <summary>The airport's taxi network: points, hold-shorts and stands as nodes; taxiway, runway and parking paths as edges.</summary>
public sealed class TaxiGraph
{
    private readonly List<TaxiEdge>[] _adjacency;

    public TaxiGraph(IReadOnlyList<TaxiNode> nodes, IReadOnlyList<TaxiEdge> edges, IReadOnlyList<Stand> stands)
    {
        Nodes = nodes;
        Edges = edges;
        Stands = stands;
        _adjacency = new List<TaxiEdge>[nodes.Count];
        for (int i = 0; i < nodes.Count; i++) _adjacency[i] = [];
        foreach (var e in edges)
        {
            // Paths have no direction in the sim's data: both ways are allowed.
            _adjacency[e.From].Add(e);
            _adjacency[e.To].Add(e with { From = e.To, To = e.From });
        }
    }

    public IReadOnlyList<TaxiNode> Nodes { get; }
    public IReadOnlyList<TaxiEdge> Edges { get; }
    public IReadOnlyList<Stand> Stands { get; }

    public IReadOnlyList<TaxiEdge> From(int node) => _adjacency[node];

    /// <summary>Nearest node that has at least one taxiway or parking edge, within <paramref name="maxM"/>.</summary>
    public TaxiNode? Nearest(GeoPoint p, double maxM = 300, Func<TaxiNode, bool>? where = null)
    {
        TaxiNode? best = null;
        double bestM = maxM;
        foreach (var n in Nodes)
        {
            if (!_adjacency[n.Id].Any(e => e.Kind != TaxiEdgeKind.Runway) || (where is not null && !where(n))) continue;
            double m = GeoMath.DistanceM(p, n.Position);
            if (m <= bestM)
            {
                best = n;
                bestM = m;
            }
        }
        return best;
    }
}
