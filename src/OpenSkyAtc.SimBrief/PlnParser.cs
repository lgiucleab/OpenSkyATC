using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;

namespace OpenSkyAtc.SimBrief;

/// <summary>One en-route point of a .pln. <see cref="Position"/> is null in MSFS 2024 files, which name points only by ident and region.</summary>
/// <param name="Kind">'W' intersection, 'V' VOR, 'N' NDB, 'A' airport, 'U' user point (the sim's facility type letters).</param>
public sealed record PlnWaypoint(string Ident, string? Region, char Kind, GeoPoint? Position, string? Airway);

/// <summary>An MSFS flight plan file, before callsign and waypoint positions are known.</summary>
public sealed record PlnFile(
    string Origin,
    string Destination,
    int? CruiseFt,
    string? DepartureRunway,
    string? ArrivalRunway,
    string? Sid,
    string? Star,
    IReadOnlyList<PlnWaypoint> Waypoints)
{
    /// <summary>
    /// The plan with this callsign. Points without a position are looked up with <paramref name="resolve"/> (the
    /// sim's navdata); points it cannot find are left out. The cruise altitude is a flight level at or above
    /// <paramref name="transitionAltitudeFt"/> (the origin's, from the sim; 18,000 ft when unknown).
    /// </summary>
    public FlightPlan ToFlightPlan(string callsign, string? telephony, string? aircraftType,
        Func<PlnWaypoint, GeoPoint?>? resolve = null, int? transitionAltitudeFt = null)
    {
        var route = new List<RouteWaypoint>();
        foreach (var w in Waypoints)
            if ((w.Position ?? resolve?.Invoke(w)) is { } p) route.Add(new RouteWaypoint(w.Ident, p, w.Airway));

        int ta = transitionAltitudeFt ?? 18_000;
        return new FlightPlan
        {
            Callsign = callsign,
            Telephony = telephony,
            AircraftType = aircraftType,
            Origin = Origin,
            Destination = Destination,
            Route = route,
            CruiseLevel = CruiseFt is { } ft and > 0 ? ft >= ta ? AltitudeTarget.FlightLevel((int)Math.Round(ft / 100.0)) : AltitudeTarget.Feet(ft) : null,
            PlannedSid = Sid,
            PlannedStar = Star,
            DepartureRunway = DepartureRunway,
            ArrivalRunway = ArrivalRunway,
        };
    }
}

/// <summary>
/// Reads MSFS .pln flight plans (SimBase.Document / FlightPlan.FlightPlan): the MSFS 2024 layout (runways and
/// procedures in DepartureDetails / ArrivalDetails, waypoints by ident and region) and the older one (WorldPosition
/// per waypoint, DepartureFP / ArrivalFP and runways on the waypoints).
/// </summary>
public static partial class PlnParser
{
    public static PlnFile Parse(string xml)
    {
        XElement plan;
        try
        {
            plan = XDocument.Parse(xml).Root?.Element("FlightPlan.FlightPlan")
                ?? throw new FlightPlanImportException(FlightPlanImportError.BadResponse, "This is not an MSFS flight plan (.pln): no FlightPlan.FlightPlan element.");
        }
        catch (XmlException e)
        {
            throw new FlightPlanImportException(FlightPlanImportError.BadResponse, $"This .pln file is not valid XML ({e.Message}).", e);
        }

        string? origin = Value(plan, "DepartureID"), destination = Value(plan, "DestinationID");
        if (origin is null || destination is null)
            throw new FlightPlanImportException(FlightPlanImportError.NoFlightPlan, "This .pln file has no departure or destination airport.");

        var points = plan.Elements("ATCWaypoint").ToList();
        var departure = plan.Element("DepartureDetails");
        var arrival = plan.Element("ArrivalDetails");
        var first = points.FirstOrDefault(p => Value(p, "ATCWaypointType") == "Airport" && Ident(p) == origin);
        var last = points.LastOrDefault(p => Value(p, "ATCWaypointType") == "Airport" && Ident(p) == destination);

        var waypoints = points
            .Where(p => Value(p, "ATCWaypointType") != "Airport")
            .Select(p => new PlnWaypoint(
                Ident(p) ?? p.Attribute("id")?.Value ?? "?",
                Value(p.Element("ICAO"), "ICAORegion"),
                KindOf(Value(p, "ATCWaypointType")),
                Value(p, "WorldPosition") is { } wp ? WorldPosition(wp) : null,
                Value(p, "ATCAirway")))
            .ToList();

        return new PlnFile(
            origin.ToUpperInvariant(),
            destination.ToUpperInvariant(),
            int.TryParse(Value(plan, "CruisingAlt"), NumberStyles.Float, CultureInfo.InvariantCulture, out int alt) ? alt : null,
            Runway(departure) ?? Runway(first),
            Runway(arrival) ?? Runway(last),
            Value(departure, "DepartureFP") ?? points.Select(p => Value(p, "DepartureFP")).FirstOrDefault(v => v is not null),
            Value(arrival, "ArrivalFP") ?? points.Select(p => Value(p, "ArrivalFP")).FirstOrDefault(v => v is not null),
            waypoints);
    }

    private static string? Ident(XElement p) => Value(p.Element("ICAO"), "ICAOIdent");

    private static char KindOf(string? type) => type switch
    {
        "VOR" => 'V',
        "NDB" => 'N',
        "Airport" => 'A',
        "User" => 'U',
        _ => 'W',
    };

    /// <summary>RunwayNumberFP 3 + RunwayDesignatorFP LEFT -> "03L".</summary>
    private static string? Runway(XElement? e)
    {
        if (!int.TryParse(Value(e, "RunwayNumberFP"), out int number) || number is < 1 or > 36) return null;
        string side = Value(e, "RunwayDesignatorFP")?.ToUpperInvariant() switch
        {
            "LEFT" or "L" => "L",
            "RIGHT" or "R" => "R",
            "CENTER" or "CENTRE" or "C" => "C",
            "WATER" or "W" => "W",
            _ => "",
        };
        return $"{number:00}{side}";
    }

    /// <summary>"N40° 28' 20.39",W3° 33' 39.08",+002000.00" -> 40.4723, -3.5609.</summary>
    internal static GeoPoint? WorldPosition(string text)
    {
        var parts = DmsPattern().Matches(text);
        if (parts.Count < 2) return null;
        double Angle(Match m)
        {
            double v = double.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture)
                + double.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture) / 60
                + double.Parse(m.Groups["s"].Value, CultureInfo.InvariantCulture) / 3600;
            return m.Groups["h"].Value is "S" or "W" ? -v : v;
        }
        return new GeoPoint(Angle(parts[0]), Angle(parts[1]));
    }

    private static string? Value(XElement? e, string name) => e?.Element(name)?.Value.Trim() is { Length: > 0 } v ? v : null;

    [GeneratedRegex("""(?<h>[NSEW])\s*(?<d>\d+(?:\.\d+)?)°\s*(?<m>\d+(?:\.\d+)?)'\s*(?<s>\d+(?:\.\d+)?)"?""")]
    private static partial Regex DmsPattern();
}
