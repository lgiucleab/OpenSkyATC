using OpenSkyAtc.Core.Geo;

namespace OpenSkyAtc.TaxiArrows;

/// <summary>
/// A taxi route as a polyline with distances along it, in metres. Taxi legs are short, so each leg is treated as
/// flat (east/north metres from its start), which is accurate to centimetres at airport scale.
/// </summary>
public sealed class TaxiPath
{
    private const double TieM = 5;

    private readonly GeoPoint[] _points;
    private readonly double[] _along;

    public TaxiPath(IReadOnlyList<GeoPoint> points)
    {
        _points = points.ToArray();
        _along = new double[_points.Length];
        for (int i = 1; i < _points.Length; i++) _along[i] = _along[i - 1] + GeoMath.DistanceM(_points[i - 1], _points[i]);
    }

    public IReadOnlyList<GeoPoint> Points => _points;
    public double LengthM => _along.Length == 0 ? 0 : _along[^1];

    /// <summary>The point <paramref name="s"/> metres along the route, and the true track there.</summary>
    public (GeoPoint Position, double TrackDeg) At(double s)
    {
        if (_points.Length == 0) throw new InvalidOperationException("Empty route.");
        if (_points.Length == 1) return (_points[0], 0);
        s = Math.Clamp(s, 0, LengthM);
        int i = Leg(s);
        double len = _along[i + 1] - _along[i];
        double track = GeoMath.BearingDeg(_points[i], _points[i + 1]);
        var p = len <= 0 ? _points[i] : GeoMath.Destination(_points[i], track, s - _along[i]);
        return (p, track);
    }

    /// <summary>
    /// Distance along the route of the point nearest to <paramref name="p"/>, looking only at or beyond
    /// <paramref name="fromM"/> metres (so progress never jumps back); of legs within 5 m of the nearest, the earliest.
    /// </summary>
    public double Along(GeoPoint p, double fromM = 0)
    {
        if (_points.Length < 2) return 0;
        var candidates = new List<(double Along, double Off)>();
        for (int i = 0; i + 1 < _points.Length; i++)
        {
            if (_along[i + 1] < fromM) continue;
            double len = _along[i + 1] - _along[i];
            double t = 0;
            if (len > 0)
            {
                var (e, n) = Local(_points[i], p);
                var (de, dn) = Local(_points[i], _points[i + 1]);
                t = Math.Clamp((e * de + n * dn) / (len * len), 0, 1);
            }
            double s = Math.Max(fromM, _along[i] + t * len);
            candidates.Add((s, GeoMath.DistanceM(At(s).Position, p)));
        }
        if (candidates.Count == 0) return fromM;

        // Where two legs are about as close (an out-and-back route), the earlier one: progress never skips ahead.
        double best = candidates.Min(c => c.Off);
        return candidates.Where(c => c.Off <= best + TieM).Min(c => c.Along);
    }

    /// <summary>Distance from <paramref name="p"/> to the route.</summary>
    public double OffsetM(GeoPoint p) => _points.Length == 0 ? double.MaxValue : GeoMath.DistanceM(At(Along(p)).Position, p);

    private int Leg(double s)
    {
        for (int i = 0; i + 1 < _along.Length; i++)
            if (s <= _along[i + 1]) return i;
        return _along.Length - 2;
    }

    private static (double E, double N) Local(GeoPoint origin, GeoPoint p)
    {
        double d = GeoMath.DistanceM(origin, p), b = GeoMath.BearingDeg(origin, p) * Math.PI / 180;
        return (d * Math.Sin(b), d * Math.Cos(b));
    }
}
