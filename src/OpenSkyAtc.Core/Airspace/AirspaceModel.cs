using System.Globalization;
using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Random;

namespace OpenSkyAtc.Core.Airspace;

/// <summary>
/// A CENTER-type frequency found in the sim's airport facility data (SimConnect frequency type 10). The host
/// collects these along the route before the flight; they are the only source of area-control frequencies.
/// </summary>
/// <param name="ChannelName">The facility's channel name for the frequency, when it has one.</param>
/// <param name="AirportName">Name used when there is no channel name ("Madrid" -> "Madrid Control").</param>
public sealed record CenterFrequency(int FrequencyKhz, GeoPoint Position, string AirportIcao, string? ChannelName = null, string? AirportName = null);

/// <summary>A generated area-control sector: a stretch of the route between two along-route distances.</summary>
public sealed record CtrSector(ControllerUnit Unit, double StartNm, double EndNm);

public sealed record AirspaceOptions
{
    /// <summary>Departure owns the aircraft within this distance of the origin (and below <see cref="TerminalCeilingFt"/>).</summary>
    public double TerminalRadiusNm { get; init; } = 40;

    public double TerminalCeilingFt { get; init; } = 15_000;

    /// <summary>Flights shorter than this skip CTR: Departure hands straight to Approach.</summary>
    public double ShortFlightNm { get; init; } = 80;

    public (double Min, double Max) SectorLengthNm { get; init; } = (150, 250);

    /// <summary>Furthest a CENTER frequency may be from a sector's midpoint (decision A, step 6).</summary>
    public double CenterSearchNm { get; init; } = 300;

    /// <summary>Handoff this far before a sector boundary.</summary>
    public double HandoffBeforeBoundaryNm { get; init; } = 5;
}

/// <summary>
/// The controller units for the flight and where each owns the aircraft. Simple geometric volumes, built once
/// at session start: terminal units around both airports, generated CTR sectors along the route between them.
/// </summary>
public sealed class AirspaceModel
{
    private readonly List<ControllerUnit> _units = [];
    private readonly List<CtrSector> _sectors = [];

    private AirspaceModel(AirspaceOptions options, RouteLine route)
    {
        Options = options;
        Route = route;
    }

    public AirspaceOptions Options { get; }
    public RouteLine Route { get; }

    public ControllerUnit? OriginDelivery { get; private init; }
    public ControllerUnit? OriginGround { get; private init; }
    public ControllerUnit? OriginTower { get; private init; }
    public ControllerUnit? OriginDeparture { get; private init; }
    public ControllerUnit? OriginApproach { get; private init; }
    public ControllerUnit? DestinationGround { get; private init; }
    public ControllerUnit? DestinationTower { get; private init; }
    public ControllerUnit? DestinationApproach { get; private init; }

    public IReadOnlyList<ControllerUnit> Units => _units;
    public IReadOnlyList<CtrSector> Sectors => _sectors;

    /// <summary>Gives the IFR clearance: Delivery, else Ground, else Tower.</summary>
    public ControllerUnit? ClearanceUnit => OriginDelivery ?? OriginGround ?? OriginTower;

    /// <summary>Handles push and taxi: Ground, else Tower.</summary>
    public ControllerUnit? GroundUnit => OriginGround ?? OriginTower;

    /// <summary>First radar unit after takeoff: Departure, else Approach (design doc fallback).</summary>
    public ControllerUnit? DepartureUnit => OriginDeparture ?? OriginApproach;

    public ControllerUnit? ArrivalApproachUnit => DestinationApproach;
    public ControllerUnit? ArrivalTowerUnit => DestinationTower;
    public ControllerUnit? ArrivalGroundUnit => DestinationGround ?? DestinationTower;

    /// <summary>The unit on a frequency, or null when nobody is there. ATIS is not a unit you talk to.</summary>
    /// <summary>The unit a call on <paramref name="khz"/> reaches: its main frequency first, then the other frequencies.</summary>
    public ControllerUnit? UnitOn(int khz) =>
        _units.FirstOrDefault(u => u.FrequencyKhz == khz && u.Role != ControllerRole.Atis)
        ?? _units.FirstOrDefault(u => u.Answers(khz) && u.Role != ControllerRole.Atis);

    /// <summary>The CTR sector covering an along-route distance, or null outside them (or when there are none).</summary>
    public CtrSector? SectorAt(double alongNm) =>
        _sectors.FirstOrDefault(s => alongNm >= s.StartNm && alongNm < s.EndNm) ?? (alongNm >= _sectors.LastOrDefault()?.EndNm ? _sectors[^1] : null);

    public CtrSector? SectorAfter(CtrSector sector)
    {
        int i = _sectors.IndexOf(sector);
        return i >= 0 && i + 1 < _sectors.Count ? _sectors[i + 1] : null;
    }

    /// <summary>
    /// The unit for the en route part of the flight at a position: the CTR sector there, the first sector before
    /// it starts, or Departure when the flight has no sectors.
    /// </summary>
    public ControllerUnit? EnRouteUnitAt(GeoPoint position)
    {
        if (_sectors.Count == 0) return DepartureUnit;
        return (SectorAt(Route.AlongNm(position)) ?? _sectors[0]).Unit;
    }

    /// <summary>Arrival volume: within the terminal radius of the destination, or below the terminal ceiling and inbound.</summary>
    public bool InArrivalVolume(GeoPoint position, double altitudeFt)
    {
        double toDest = Route.RemainingNm(position);
        return toDest <= Options.TerminalRadiusNm || (altitudeFt < Options.TerminalCeilingFt && toDest < Route.AlongNm(position));
    }

    /// <summary>Departure volume: within the terminal radius of the origin and below the terminal ceiling.</summary>
    public bool InDepartureVolume(GeoPoint position, double altitudeFt) =>
        Route.AlongNm(position) < Options.TerminalRadiusNm && altitudeFt < Options.TerminalCeilingFt;

    public static AirspaceModel Build(AirportData origin, AirportData destination, FlightPlan plan,
        IReadOnlyList<CenterFrequency> centers, IRandom rng, AirspaceOptions? options = null)
    {
        options ??= new AirspaceOptions();
        var route = RouteLine.From(origin, destination, plan);

        ControllerUnit? Unit(AirportData airport, ControllerRole role)
        {
            var all = airport.Frequencies.Where(x => x.Role == role).ToList();
            if (all.Count == 0) return null;
            var f = all[0];
            var others = all.Skip(1).Select(x => x.FrequencyKhz).Where(k => k != f.FrequencyKhz).Distinct().ToList();
            return new ControllerUnit(role, f.Callsign ?? $"{airport.Name ?? airport.Icao} {RoleName(role)}", f.FrequencyKhz,
                OtherFrequenciesKhz: others.Count > 0 ? others : null);
        }

        var model = new AirspaceModel(options, route)
        {
            OriginDelivery = Unit(origin, ControllerRole.Delivery),
            OriginGround = Unit(origin, ControllerRole.Ground),
            OriginTower = Unit(origin, ControllerRole.Tower),
            OriginDeparture = Unit(origin, ControllerRole.Departure),
            OriginApproach = Unit(origin, ControllerRole.Approach),
            DestinationGround = Unit(destination, ControllerRole.Ground),
            DestinationTower = Unit(destination, ControllerRole.Tower),
            DestinationApproach = Unit(destination, ControllerRole.Approach),
        };
        foreach (var u in new[]
                 {
                     model.OriginDelivery, model.OriginGround, model.OriginTower, model.OriginDeparture, model.OriginApproach,
                     model.DestinationGround, model.DestinationTower, model.DestinationApproach,
                 })
        {
            if (u is not null && !model._units.Contains(u)) model._units.Add(u);
        }

        foreach (var sector in BuildSectors(route, centers, rng, options))
        {
            model._sectors.Add(sector);
            if (!model._units.Contains(sector.Unit)) model._units.Add(sector.Unit);
        }
        return model;
    }

    /// <summary>
    /// Cuts the route between the two terminal areas into sectors of 150-250 nm, gives each the nearest CENTER
    /// frequency within the search radius, merges sectors without one into a neighbour, and merges neighbours on
    /// the same frequency. Never invents a frequency.
    /// </summary>
    private static List<CtrSector> BuildSectors(RouteLine route, IReadOnlyList<CenterFrequency> centers, IRandom rng, AirspaceOptions o)
    {
        double start = o.TerminalRadiusNm, end = route.LengthNm - o.TerminalRadiusNm;
        if (route.LengthNm < o.ShortFlightNm || end <= start || centers.Count == 0) return [];

        var bounds = new List<(double Start, double End)>();
        for (double s = start; s < end;)
        {
            double len = o.SectorLengthNm.Min + rng.NextDouble() * (o.SectorLengthNm.Max - o.SectorLengthNm.Min);
            double e = Math.Min(end, s + len);
            if (end - e < o.SectorLengthNm.Min / 2) e = end; // a short remainder joins this sector
            bounds.Add((s, e));
            s = e;
        }

        var withFreq = bounds.Select(b =>
        {
            var mid = route.PointAt((b.Start + b.End) / 2);
            var nearest = centers
                .Select(c => (Center: c, Nm: GeoMath.DistanceNm(c.Position, mid)))
                .Where(x => x.Nm <= o.CenterSearchNm)
                .OrderBy(x => x.Nm)
                .ThenBy(x => x.Center.FrequencyKhz)
                .FirstOrDefault();
            return (b.Start, b.End, nearest.Center);
        }).ToList();

        if (withFreq.All(x => x.Center is null)) return [];

        // Sectors without a frequency take the previous sector's (or, at the start, the next one's).
        for (int i = 0; i < withFreq.Count; i++)
        {
            if (withFreq[i].Center is not null) continue;
            var donor = withFreq.Take(i).LastOrDefault(x => x.Center is not null).Center ?? withFreq.Skip(i).First(x => x.Center is not null).Center;
            withFreq[i] = withFreq[i] with { Center = donor };
        }

        var sectors = new List<CtrSector>();
        foreach (var (s, e, c) in withFreq)
        {
            var unit = new ControllerUnit(ControllerRole.Center, SectorName(c!), c!.FrequencyKhz);
            if (sectors.Count > 0 && sectors[^1].Unit.FrequencyKhz == unit.FrequencyKhz)
                sectors[^1] = sectors[^1] with { EndNm = e };
            else
                sectors.Add(new CtrSector(unit, s, e));
        }
        return sectors;
    }

    /// <summary>"MADRID CENTER" -> "Madrid Control" (ICAO term for area control); no channel name -> "&lt;airport&gt; Control".</summary>
    public static string SectorName(CenterFrequency c)
    {
        string raw = c.ChannelName is { Length: > 0 } ch ? ch : $"{c.AirportName ?? c.AirportIcao} Control";
        var words = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 1 && words[^1].ToUpperInvariant() is "CENTER" or "CENTRE" or "CTR" or "ACC" or "CONTROL") words[^1] = "Control";
        else words.Add("Control");
        var ti = CultureInfo.InvariantCulture.TextInfo;
        return string.Join(" ", words.Select(w => w == "Control" ? w : ti.ToTitleCase(w.ToLowerInvariant())));
    }

    public static string RoleName(ControllerRole role) => role switch
    {
        ControllerRole.Atis => "Information",
        ControllerRole.Delivery => "Delivery",
        ControllerRole.Ground => "Ground",
        ControllerRole.Tower => "Tower",
        ControllerRole.Departure => "Departure",
        ControllerRole.Approach => "Approach",
        ControllerRole.Center => "Control",
        _ => role.ToString(),
    };
}

/// <summary>The planned route as a line: origin, the plan's waypoints, destination. Distances in nm along it.</summary>
public sealed class RouteLine
{
    private readonly GeoPoint[] _points;
    private readonly double[] _cumulative;

    private RouteLine(GeoPoint[] points)
    {
        _points = points;
        _cumulative = new double[points.Length];
        for (int i = 1; i < points.Length; i++) _cumulative[i] = _cumulative[i - 1] + GeoMath.DistanceNm(points[i - 1], points[i]);
    }

    public IReadOnlyList<GeoPoint> Points => _points;
    public double LengthNm => _cumulative.Length == 0 ? 0 : _cumulative[^1];
    public GeoPoint Origin => _points[0];
    public GeoPoint Destination => _points[^1];

    public static RouteLine From(AirportData origin, AirportData destination, FlightPlan plan)
    {
        var points = new List<GeoPoint>();
        if (origin.ReferencePoint is { } o) points.Add(o);
        points.AddRange(plan.Route.Select(w => w.Position));
        if (destination.ReferencePoint is { } d) points.Add(d);
        if (points.Count == 0) points.Add(new GeoPoint(0, 0));
        if (points.Count == 1) points.Add(points[0]);
        return new RouteLine(points.ToArray());
    }

    /// <summary>Distance flown along the route at the point nearest to <paramref name="p"/>.</summary>
    public double AlongNm(GeoPoint p)
    {
        double best = double.MaxValue, along = 0;
        for (int i = 0; i + 1 < _points.Length; i++)
        {
            double len = _cumulative[i + 1] - _cumulative[i];
            var (xt, at) = len > 0 ? GeoMath.TrackOffsets(_points[i], _points[i + 1], p) : (GeoMath.DistanceNm(_points[i], p), 0);
            double clamped = Math.Clamp(at, 0, len);
            double off = at < 0 ? GeoMath.DistanceNm(_points[i], p) : at > len ? GeoMath.DistanceNm(_points[i + 1], p) : Math.Abs(xt);
            if (off < best)
            {
                best = off;
                along = _cumulative[i] + clamped;
            }
        }
        return along;
    }

    /// <summary>Great-circle distance to the destination.</summary>
    public double RemainingNm(GeoPoint p) => GeoMath.DistanceNm(p, Destination);

    public GeoPoint PointAt(double alongNm)
    {
        alongNm = Math.Clamp(alongNm, 0, LengthNm);
        for (int i = 0; i + 1 < _points.Length; i++)
        {
            if (alongNm <= _cumulative[i + 1] || i + 2 == _points.Length)
            {
                double into = alongNm - _cumulative[i];
                return GeoMath.Destination(_points[i], GeoMath.BearingDeg(_points[i], _points[i + 1]), into * GeoMath.MetresPerNm);
            }
        }
        return _points[^1];
    }
}
