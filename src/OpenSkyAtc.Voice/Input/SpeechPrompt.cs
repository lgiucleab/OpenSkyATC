using OpenSkyAtc.Core.Conversation;

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

    /// <summary>
    /// The flight's prompt for one call: when ATC waits for a readback, the prompt ends with that readback written the
    /// way the parser reads it ("124.230, D-KR"), since Whisper leans most on the end of its prompt. Added after the
    /// owner's first flight, where base.en turned spoken frequencies and levels into other words.
    /// </summary>
    public static string ForCall(string flightPrompt, Readback? pending, string? callsign)
    {
        if (pending is null || pending.Items.Count == 0) return flightPrompt;
        var said = pending.Items.Select(i => Written(i.Key, i.Value)).Where(s => s.Length > 0).ToList();
        if (said.Count == 0) return flightPrompt;
        if (!string.IsNullOrWhiteSpace(callsign)) said.Add(callsign);
        return $"{flightPrompt} {string.Join(", ", said)}.";
    }

    /// <summary>A readback value as Whisper should write it.</summary>
    internal static string Written(ReadbackItem item, string value) => item switch
    {
        ReadbackItem.Frequency when value.Length == 6 => $"{value[..3]}.{value[3..]}",
        ReadbackItem.Altitude or ReadbackItem.InitialClimb when value.StartsWith("FL", StringComparison.Ordinal) => $"flight level {value[2..].TrimStart('0')}",
        ReadbackItem.Altitude or ReadbackItem.InitialClimb when value.EndsWith("FT", StringComparison.Ordinal) => $"{value[..^2]} feet",
        ReadbackItem.Squawk => $"squawk {value}",
        ReadbackItem.Runway => $"runway {value}",
        ReadbackItem.HoldShorts => string.Join(", ", value.Split(',').Select(r => $"hold short runway {r}")),
        ReadbackItem.Heading => $"heading {value}",
        ReadbackItem.Speed => $"speed {value} knots",
        ReadbackItem.Qnh => $"QNH {value}",
        ReadbackItem.HoldingPoint => $"holding point {value}",
        ReadbackItem.DirectTo => $"direct {value}",
        ReadbackItem.Destination => $"cleared to {value}",
        ReadbackItem.Route => value == ReadbackValues.FlightPlannedRoute ? "flight planned route" : value,
        ReadbackItem.Clearance => value switch
        {
            ReadbackValues.Push => "push and start approved",
            ReadbackValues.LineUp => "line up and wait",
            ReadbackValues.Takeoff => "cleared for takeoff",
            ReadbackValues.Land => "cleared to land",
            ReadbackValues.Cross => "cross",
            ReadbackValues.Approach => "cleared approach",
            _ => "",
        },
        ReadbackItem.Facing => $"facing {value.ToLowerInvariant()}",
        _ => value,
    };
}
