using System.Globalization;
using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Messages;
using OpenSkyAtc.Core.Model;

namespace OpenSkyAtc.Phraseology;

/// <summary>
/// Renders an <see cref="AtcMessage"/> as display text: "Vueling 1234, cleared to LEMD via flight planned route, ...".
/// Step 3 version: one fixed wording per part. Step 4 adds speech text, the ICAO number rules and variety.
/// </summary>
public static class DisplayRenderer
{
    public static string Render(AtcMessage message) =>
        $"{Callsign(message.Callsign)}, {string.Join(", ", message.Parts.Select(Part))}";

    /// <summary>"Vueling 1234" from VLG1234 + "Vueling"; the filed code when the telephony is unknown.</summary>
    public static string Callsign(AircraftCallsign callsign)
    {
        if (callsign.Telephony is not { } telephony) return callsign.Code;
        string number = callsign.Code.Length > 3 ? callsign.Code[3..] : callsign.Code;
        return $"{telephony} {number}";
    }

    public static string Part(AtcMessagePart part) => part switch
    {
        IfrClearance c => $"cleared to {c.Destination} via {Route(c.Sid)}, climb {Altitude(c.InitialClimb)}, squawk {Code(c.Squawk)}",
        ClearedTo c => $"cleared to {c.Destination}",
        Via v => $"via {Route(v.Sid)}",
        ClimbTo c => $"climb {Altitude(c.Altitude)}",
        Squawk s => $"squawk {Code(s.Code)}",
        ReadbackCorrect => "readback correct",
        Contact { Condition: ContactCondition.WhenReadyForPushAndStart } c => $"when ready for push and start contact {c.Unit} {Frequency(c.FrequencyKhz)}",
        Contact c => $"contact {c.Unit} {Frequency(c.FrequencyKhz)}",
        PushStartApproved => "push and start approved",
        TaxiVia t => Taxi(t),
        TaxiHoldingPoint h => $"holding point {h.Name}",
        RunwayIs r => $"runway {r.Runway}",
        HoldShort h => $"hold short runway {h.Runway}",
        LineUpAndWait l => $"runway {l.Runway}, line up and wait",
        Wind { SpeedKt: 0 } => "wind calm",
        Wind w => $"wind {w.DirectionMagDeg:000} degrees {w.SpeedKt} knots",
        ClearedTakeoff t => $"runway {t.Runway}, cleared for takeoff",
        Negative => "negative",
        ReadBackRequest r => $"read back the {string.Join(" and ", r.Items.Select(ItemName))}",
        StandBy => "stand by",
        SayAgainRequest => "say again",
        UnableContact u => $"unable, contact {u.Unit} {Frequency(u.FrequencyKhz)}",
        Roger => "roger",
        EmergencyAcknowledged { Kind: EmergencyKind.Mayday } => "roger mayday",
        EmergencyAcknowledged => "roger pan pan",
        _ => part.ToString(),
    };

    public static string ItemName(ReadbackItem item) => item switch
    {
        ReadbackItem.Destination => "destination",
        ReadbackItem.Route => "route",
        ReadbackItem.InitialClimb => "initial climb",
        ReadbackItem.Squawk => "squawk",
        ReadbackItem.HoldingPoint => "holding point",
        ReadbackItem.Runway => "runway",
        ReadbackItem.HoldShorts => "hold short",
        ReadbackItem.Clearance => "clearance",
        ReadbackItem.Altitude => "altitude",
        ReadbackItem.Heading => "heading",
        ReadbackItem.Speed => "speed",
        ReadbackItem.DirectTo => "direct",
        ReadbackItem.Frequency => "frequency",
        ReadbackItem.Qnh => "QNH",
        _ => item.ToString(),
    };

    public static string Altitude(AltitudeTarget alt) => alt.Kind == AltitudeKind.FlightLevel
        ? $"FL{alt.Value:000}"
        : $"{alt.Value.ToString(CultureInfo.InvariantCulture)} ft";

    public static string Frequency(int khz) => (khz / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);

    private static string Code(int squawk) => squawk.ToString("0000", CultureInfo.InvariantCulture);

    private static string Route(string? sid) => sid is null ? "flight planned route" : $"{sid} departure";

    private static string Taxi(TaxiVia t)
    {
        string target = t.HoldingPoint is { } hp ? $"holding point {hp} runway {t.Runway}" : $"holding point runway {t.Runway}";
        string via = t.Segments.Count > 0 ? $" via {string.Join(", ", t.Segments)}" : "";
        string holds = string.Concat(t.HoldShorts.Select(h => $", hold short runway {h}"));
        return $"taxi to {target}{via}{holds}";
    }
}
