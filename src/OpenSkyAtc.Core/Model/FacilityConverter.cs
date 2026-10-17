using System.Globalization;
using OpenSkyAtc.Core.Airspace;
using OpenSkyAtc.Core.Geo;

namespace OpenSkyAtc.Core.Model;

/// <summary>
/// Turns the sim's facility data (<see cref="FacilityDump"/>) into the brain's <see cref="AirportData"/>, including
/// the taxi graph. Used for the live sim (any airport the player has) and for fixtures. Enum numbers are the
/// SimConnect values from the SDK's SimConnect_AddToFacilityDefinition page.
/// </summary>
public static class FacilityConverter
{
    private const double FtPerM = 3.28084;
    private const double HoldShortRunwayM = 150;

    public static AirportData ToAirportData(FacilityDump d)
    {
        var runways = d.Runways.Select(Runway).ToList();
        string? place = PlaceName(d);
        var graph = BuildGraph(d, runways);

        var holdNodes = graph.Nodes.Where(n => n.Kind == TaxiNodeKind.HoldShort && n.RunwayDesignator is not null).ToList();
        var holdShorts = holdNodes.Select(n => new HoldShortPoint(n.Position, n.RunwayDesignator!)).ToList();
        // A hold-short serves both directions of its runway as a holding point.
        var holdingPoints = holdNodes.SelectMany(n =>
        {
            var rwy = runways.First(r => r.Has(n.RunwayDesignator!));
            return new[] { new HoldingPoint(null, n.Position, rwy.First.Designator), new HoldingPoint(null, n.Position, rwy.Second.Designator) };
        }).ToList();

        return new AirportData
        {
            Icao = d.Icao,
            Name = place,
            ElevationFt = Math.Round(d.ElevationM * FtPerM),
            Location = new GeoPoint(d.Latitude, d.Longitude),
            Runways = runways.Select(r => WithApproaches(r, d)).ToList(),
            HoldShorts = holdShorts,
            HoldingPoints = holdingPoints,
            Frequencies = Frequencies(d, place),
            TransitionAltitudeFt = d.TransitionAltitudeM > 0 ? (int)(Math.Round(d.TransitionAltitudeM * FtPerM / 100) * 100) : null,
            TransitionLevel = d.TransitionLevelM > 0 ? (int)Math.Round(d.TransitionLevelM * FtPerM / 100) : null,
            Sids = Sids(d),
            Stars = d.Arrivals.Distinct().ToList(),
            Taxi = graph,
        };
    }

    /// <summary>The sim's CENTER frequencies at this airport, for the airspace model.</summary>
    public static IEnumerable<CenterFrequency> CenterFrequencies(FacilityDump d) =>
        d.Frequencies.Where(f => f.Type == 10)
            .Select(f => new CenterFrequency(Khz(f.FrequencyHz), new GeoPoint(d.Latitude, d.Longitude), d.Icao, f.Name, PlaceName(d)));

    /// <summary>
    /// Name for callsigns and the spoken destination. The sim's city field is a localisation key, so the name on the
    /// approach/departure frequency (usually the city, "MADRID") is used, then the airport name ("Barajas").
    /// </summary>
    private static string? PlaceName(FacilityDump d)
    {
        string? fromRadar = d.Frequencies.Where(f => f.Type is 8 or 9).Select(f => StationName(f.Name, d.Icao)).FirstOrDefault(n => n is not null);
        string? raw = fromRadar ?? d.Name;
        return raw is null ? null : TitleCase(raw);
    }

    private static readonly HashSet<string> RoleWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "CONTROL", "RADAR", "APPROACH", "APP", "DEPARTURE", "DEP", "DIRECTOR", "TOWER", "TWR", "GROUND", "GND",
        "DELIVERY", "DEL", "CLEARANCE", "CLR", "INFORMATION", "INFO", "ATIS",
    };

    /// <summary>
    /// The place part of a sim frequency name: trailing role words dropped ("PALMA CONTROL" -> "PALMA", so the
    /// callsign is not "Palma Control Approach"); null when nothing is left or it is just the ICAO code (ATIS
    /// frequencies are often named "LFPG").
    /// </summary>
    internal static string? StationName(string? raw, string icao)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var words = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (words.Count > 1 && RoleWords.Contains(words[^1])) words.RemoveAt(words.Count - 1);
        string name = string.Join(' ', words);
        return name.Equals(icao, StringComparison.OrdinalIgnoreCase) || RoleWords.Contains(name) ? null : name;
    }

    private static List<AirportFrequency> Frequencies(FacilityDump d, string? place)
    {
        var list = new List<AirportFrequency>();
        string? tower = d.Frequencies.Where(f => f.Type == 6).Select(f => StationName(f.Name, d.Icao)).FirstOrDefault(n => n is not null);
        foreach (var f in d.Frequencies)
        {
            ControllerRole? role = f.Type switch
            {
                1 => ControllerRole.Atis,
                5 => ControllerRole.Ground,
                6 => ControllerRole.Tower,
                7 => ControllerRole.Delivery,
                8 => ControllerRole.Approach,
                9 => ControllerRole.Departure,
                _ => null, // CENTER goes to the airspace model; UNICOM/CTAF/AWOS etc. are not ATC units here
            };
            if (role is not { } r) continue;
            string name = StationName(f.Name, d.Icao) is { } n ? TitleCase(n) : tower is not null ? TitleCase(tower) : place ?? d.Icao;
            list.Add(new AirportFrequency(r, Khz(f.FrequencyHz), $"{name} {AirspaceModel.RoleName(r)}"));
        }
        return list;
    }

    private static int Khz(int hz) => (int)Math.Round(hz / 1000.0);

    /// <summary>SIDs with the runway ends they start from (runway numbers 37-44 are compass directions, not runways).</summary>
    private static List<SidInfo> Sids(FacilityDump d)
    {
        var byName = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < d.Departures.Count; i++)
        {
            if (!byName.TryGetValue(d.Departures[i], out var runways)) byName[d.Departures[i]] = runways = new(StringComparer.OrdinalIgnoreCase);
            if (i < d.DepartureRunways.Count)
                foreach (var t in d.DepartureRunways[i])
                    if (t.Length == 2 && t[0] is >= 1 and <= 36) runways.Add(Designator(t[0], t[1]));
        }
        return byName.Select(kv => new SidInfo(kv.Key, null, kv.Value.Order(StringComparer.Ordinal).ToList())).ToList();
    }

    public static string Designator(int number, int designator)
    {
        string n = number switch
        {
            >= 1 and <= 36 => number.ToString("00", CultureInfo.InvariantCulture),
            37 => "N", 38 => "NE", 39 => "E", 40 => "SE", 41 => "S", 42 => "SW", 43 => "W", 44 => "NW",
            _ => number.ToString(CultureInfo.InvariantCulture),
        };
        string s = designator switch { 1 => "L", 2 => "R", 3 => "C", 5 => "A", 6 => "B", _ => "" };
        return n + s;
    }

    /// <summary>Ends from the centre, true heading and length: the primary end is the one you take off from on the heading.</summary>
    private static Runway Runway(FacilityRunway r)
    {
        var centre = new GeoPoint(r.Latitude, r.Longitude);
        var primary = GeoMath.Destination(centre, r.HeadingDeg + 180, r.LengthM / 2);
        var secondary = GeoMath.Destination(centre, r.HeadingDeg, r.LengthM / 2);
        return new Runway(
            new RunwayEnd(Designator(r.PrimaryNumber, r.PrimaryDesignator), primary, GeoMath.NormalizeDeg(r.HeadingDeg)),
            new RunwayEnd(Designator(r.SecondaryNumber, r.SecondaryDesignator), secondary, GeoMath.NormalizeDeg(r.HeadingDeg + 180)),
            r.WidthM);
    }

    /// <summary>Approach type and FAF altitude per runway end; ILS preferred, then localiser, RNP, VOR, NDB.</summary>
    private static Runway WithApproaches(Runway r, FacilityDump d)
    {
        RunwayEnd End(RunwayEnd e)
        {
            var best = d.Approaches
                .Where(a => Designator(a.RunwayNumber, a.RunwayDesignator) == e.Designator)
                .Select(a => (a, Rank: a.Type switch { 4 => 0, 5 => 1, 10 or 1 => 2, 2 or 8 => 3, 3 or 9 => 4, _ => 9 }))
                .Where(x => x.Rank < 9)
                .OrderBy(x => x.Rank)
                .Select(x => x.a)
                .FirstOrDefault();
            if (best is null) return e;
            string type = best.Type switch { 4 => "ILS", 5 => "localiser", 10 or 1 => "RNP", 2 or 8 => "VOR", _ => "NDB" };
            int? faf = best.FafAltitudeM > 0 ? (int)(Math.Round(best.FafAltitudeM * FtPerM / 100) * 100) : null;
            return e with { ApproachType = type, ApproachAltitudeFt = faf };
        }
        return r with { First = End(r.First), Second = End(r.Second) };
    }

    private static TaxiGraph BuildGraph(FacilityDump d, IReadOnlyList<Runway> runways)
    {
        int pointCount = d.TaxiPoints.Count == 0 ? 0 : d.TaxiPoints.Max(p => p.Index) + 1;
        var positions = new GeoPoint[pointCount];
        var kinds = new TaxiNodeKind[pointCount];
        foreach (var p in d.TaxiPoints)
        {
            positions[p.Index] = new GeoPoint(p.Latitude, p.Longitude);
            kinds[p.Index] = p.Type is 2 or 4 or 5 or 6 ? TaxiNodeKind.HoldShort : TaxiNodeKind.Normal;
        }

        var nodes = new List<TaxiNode>();
        for (int i = 0; i < pointCount; i++)
        {
            string? rwy = kinds[i] == TaxiNodeKind.HoldShort ? NearestRunway(positions[i], runways) : null;
            nodes.Add(new TaxiNode(i, positions[i], rwy is null && kinds[i] == TaxiNodeKind.HoldShort ? TaxiNodeKind.Normal : kinds[i], rwy));
        }

        var stands = new List<Stand>();
        var parkingNode = new Dictionary<int, int>();
        foreach (var p in d.Parkings)
        {
            int id = nodes.Count;
            var pos = new GeoPoint(p.Latitude, p.Longitude);
            nodes.Add(new TaxiNode(id, pos, TaxiNodeKind.Parking));
            parkingNode[p.Index] = id;
            stands.Add(new Stand(StandName(p), pos, p.HeadingDeg, p.RadiusM, p.Type is 8 or 9 or 10 or 15, id));
        }

        var edges = new List<TaxiEdge>();
        foreach (var e in d.TaxiPaths)
        {
            string? name = e.NameIndex < d.TaxiNames.Count && d.TaxiNames[(int)e.NameIndex] is { Length: > 0 } n ? n : null;
            switch (e.Type)
            {
                case 1 or 4 when Valid(e.Start) && Valid(e.End):
                    edges.Add(new TaxiEdge(e.Start, e.End, name, Length(e.Start, e.End), TaxiEdgeKind.Taxiway));
                    break;
                case 2 when Valid(e.Start) && Valid(e.End):
                    var rwy = runways.FirstOrDefault(r => r.Has(Designator(e.RunwayNumber, e.RunwayDesignator)));
                    edges.Add(new TaxiEdge(e.Start, e.End, null, Length(e.Start, e.End), TaxiEdgeKind.Runway, rwy?.Name));
                    break;
                case 3 when Valid(e.Start) && parkingNode.TryGetValue(e.End, out int park):
                    edges.Add(new TaxiEdge(e.Start, park, name, GeoMath.DistanceM(nodes[e.Start].Position, nodes[park].Position), TaxiEdgeKind.Parking));
                    break;
                case 6 when Valid(e.Start) && Valid(e.End):
                    // "Vehicle" lanes: some sceneries (e.g. KJFK) connect stands to taxiways only through these.
                    edges.Add(new TaxiEdge(e.Start, e.End, null, Length(e.Start, e.End), TaxiEdgeKind.Apron));
                    break;
                // 5 closed, 7 road, 8 painted line: not for aircraft.
            }
        }
        return new TaxiGraph(nodes, edges, stands);

        bool Valid(int i) => i >= 0 && i < pointCount;
        double Length(int a, int b) => GeoMath.DistanceM(positions[a], positions[b]);
    }

    /// <summary>The runway a hold-short protects: the nearest one within 150 m of its edge (one end's designator).</summary>
    private static string? NearestRunway(GeoPoint p, IReadOnlyList<Runway> runways)
    {
        Runway? best = null;
        double bestM = HoldShortRunwayM;
        foreach (var r in runways)
        {
            var (along, cross) = r.Project(p);
            double alongOut = along < 0 ? -along : along > r.LengthM ? along - r.LengthM : 0;
            double m = Math.Max(0, Math.Abs(cross) - r.WidthM / 2) + alongOut;
            if (m <= bestM)
            {
                best = r;
                bestM = m;
            }
        }
        return best?.First.Designator;
    }

    /// <summary>"T527" for GATE_T 527; "527" for PARKING/GATE 527; "N12" for N_PARKING 12. Suffix 1-26 read as A-Z (not confirmed in the SDK).</summary>
    public static string StandName(FacilityParking p)
    {
        string prefix = p.Name switch
        {
            >= 12 and <= 37 => ((char)('A' + p.Name - 12)).ToString(),
            2 => "N", 3 => "NE", 4 => "E", 5 => "SE", 6 => "S", 7 => "SW", 8 => "W", 9 => "NW",
            _ => "",
        };
        string suffix = p.Suffix is >= 1 and <= 26 ? ((char)('A' + p.Suffix - 1)).ToString() : "";
        return $"{prefix}{p.Number.ToString(CultureInfo.InvariantCulture)}{suffix}";
    }

    private static string TitleCase(string s) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.Trim().ToLowerInvariant());
}
