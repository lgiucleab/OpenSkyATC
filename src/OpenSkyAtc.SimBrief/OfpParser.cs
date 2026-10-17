using System.Globalization;
using System.Text.Json;
using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;

namespace OpenSkyAtc.SimBrief;

/// <summary>
/// Maps a SimBrief OFP (the JSON from xml.fetcher.php with json=v2) to a <see cref="FlightPlan"/>. SimBrief writes
/// numbers as strings, and in v2 <c>navlog</c> is a list of fixes and <c>alternate</c> a list; v1's object forms
/// (<c>navlog.fix</c>, a single <c>alternate</c>) are read too.
/// </summary>
public static class OfpParser
{
    public static FlightPlan Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            throw new FlightPlanImportException(FlightPlanImportError.BadResponse, "SimBrief sent something that is not a flight plan (invalid JSON).", e);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new FlightPlanImportException(FlightPlanImportError.BadResponse, "SimBrief sent something that is not a flight plan.");
            if (Text(root, "fetch", "status") is { } status && !status.Equals("Success", StringComparison.OrdinalIgnoreCase))
                throw FetchError(status);

            string? origin = Text(root, "origin", "icao_code");
            string? destination = Text(root, "destination", "icao_code");
            if (origin is null || destination is null)
                throw new FlightPlanImportException(FlightPlanImportError.NoFlightPlan, "The SimBrief plan has no origin or destination. Generate a new flight plan on SimBrief and import again.");

            string callsign = Text(root, "atc", "callsign") ?? $"{Text(root, "general", "icao_airline")}{Text(root, "general", "flight_number")}";
            if (callsign.Length == 0) throw new FlightPlanImportException(FlightPlanImportError.BadResponse, "The SimBrief plan has no callsign.");

            return new FlightPlan
            {
                Callsign = callsign.ToUpperInvariant(),
                AircraftType = Text(root, "aircraft", "icaocode") ?? Text(root, "aircraft", "icao_code"),
                Origin = origin.ToUpperInvariant(),
                Destination = destination.ToUpperInvariant(),
                Alternate = Alternate(root),
                Route = Route(root),
                CruiseLevel = Cruise(root),
                PlannedSid = Text(root, "general", "sid_ident"),
                PlannedStar = Text(root, "general", "star_ident"),
                DepartureRunway = Text(root, "origin", "plan_rwy"),
                ArrivalRunway = Text(root, "destination", "plan_rwy"),
            };
        }
    }

    /// <summary>The error for a fetch status other than "Success" ("Error: Unknown UserID", ...).</summary>
    internal static FlightPlanImportException FetchError(string status) =>
        status.Contains("user", StringComparison.OrdinalIgnoreCase)
            ? new(FlightPlanImportError.UnknownPilot, $"SimBrief does not know that Pilot ID ({status}). Check it on the SimBrief Account Settings page.")
            : new(FlightPlanImportError.NoFlightPlan, $"SimBrief has no flight plan for this pilot ({status}). Generate one on SimBrief and import again.");

    private static string? Alternate(JsonElement root)
    {
        if (!root.TryGetProperty("alternate", out var alt)) return null;
        var first = alt.ValueKind == JsonValueKind.Array ? alt.EnumerateArray().FirstOrDefault() : alt;
        return first.ValueKind == JsonValueKind.Object ? Text(first, "icao_code")?.ToUpperInvariant() : null;
    }

    /// <summary>Navlog fixes between the airports, with their airway (none for direct legs and SID/STAR legs).</summary>
    private static IReadOnlyList<RouteWaypoint> Route(JsonElement root)
    {
        if (!root.TryGetProperty("navlog", out var navlog)) return [];
        if (navlog.ValueKind == JsonValueKind.Object && navlog.TryGetProperty("fix", out var fix)) navlog = fix;
        if (navlog.ValueKind != JsonValueKind.Array) return [];

        var route = new List<RouteWaypoint>();
        foreach (var f in navlog.EnumerateArray())
        {
            if (Text(f, "type") == "apt") continue;
            if (Text(f, "ident") is not { } ident || Number(f, "pos_lat") is not { } lat || Number(f, "pos_long") is not { } lon) continue;
            string? airway = Text(f, "via_airway");
            bool sidStar = Text(f, "is_sid_star") == "1";
            route.Add(new RouteWaypoint(ident, new GeoPoint(lat, lon), airway is null or "DCT" || sidStar ? null : airway));
        }
        return route;
    }

    /// <summary>The initial cruise altitude: a flight level at or above the origin's transition altitude, feet below it.</summary>
    private static AltitudeTarget? Cruise(JsonElement root)
    {
        if (Number(root, "general", "initial_altitude") is not { } feet || feet <= 0) return null;
        double transition = Number(root, "origin", "trans_alt") ?? 18_000;
        return feet >= transition ? AltitudeTarget.FlightLevel((int)Math.Round(feet / 100)) : AltitudeTarget.Feet((int)feet);
    }

    private static string? Text(JsonElement e, params string[] path)
    {
        foreach (var p in path)
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(p, out e)) return null;
        string? s = e.ValueKind switch
        {
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number => e.GetRawText(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private static double? Number(JsonElement e, params string[] path) =>
        Text(e, path) is { } s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;
}
