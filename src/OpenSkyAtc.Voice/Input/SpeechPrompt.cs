namespace OpenSkyAtc.Voice.Input;

/// <summary>
/// Whisper's initial prompt for a flight: an example radio call written with the flight's own words, so names it
/// would otherwise spell freely (the callsign, the SID, fixes) come out the way the parser looks for them, and
/// numbers come out as digits. Whisper reads at most about 220 tokens of prompt; this stays far below.
/// </summary>
public static class SpeechPrompt
{
    /// <param name="callsign">Spoken callsign, "Iberia 1234".</param>
    /// <param name="stations">Station names, "Madrid Ground".</param>
    /// <param name="names">Other words of the flight: SID, runways, fixes, airports.</param>
    public static string Build(string? callsign, IEnumerable<string> stations, IEnumerable<string> names)
    {
        var parts = new List<string>();
        var station = stations.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
        if (station is not null) parts.Add(station);
        if (!string.IsNullOrWhiteSpace(callsign)) parts.Add(callsign);
        parts.Add("request IFR clearance, information Bravo, flight planned route, request push and start, squawk 4721, " +
                  "climb flight level 120, descend 5000 feet, QNH 1013, heading 270, reduce speed 220 knots, contact 121.635, " +
                  "Mayday, wilco");
        var words = names.Concat(stations.Skip(1)).Where(w => !string.IsNullOrWhiteSpace(w)).Distinct(StringComparer.OrdinalIgnoreCase).Take(24);
        string list = string.Join(", ", words);
        if (list.Length > 0) parts.Add(list);
        if (!string.IsNullOrWhiteSpace(callsign)) parts.Add(callsign);
        return string.Join(", ", parts) + ".";
    }
}
