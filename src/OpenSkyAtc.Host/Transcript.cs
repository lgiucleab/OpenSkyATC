using System.Globalization;
using OpenSkyAtc.Core.Conformance;
using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Phraseology;

namespace OpenSkyAtc.Host;

public enum TranscriptKind
{
    /// <summary>A controller transmission.</summary>
    Atc,

    /// <summary>The pilot's call.</summary>
    Pilot,

    /// <summary>What happened (phase change, readback verdict, taxi fallback...).</summary>
    Event,

    /// <summary>Something the pilot should notice (conformance, wrong frequency, missed readback).</summary>
    Warning,
}

/// <param name="Station">ATC unit callsign, or "nobody" for a call on a frequency without a unit.</param>
public sealed record TranscriptEntry(TimeSpan Time, TranscriptKind Kind, string? Station, int? FrequencyKhz, string Text);

/// <summary>
/// Turns new session log entries into transcript lines, rendering ATC messages in order (as the renderer needs).
/// Pilot calls are written as short radio-style phrases in English, like the radio itself, ending with
/// <paramref name="pilotCallsign"/> when given ("Runway 36L, Iberia 1234").
/// </summary>
public sealed class TranscriptWriter(FlightSession session, IcaoRenderer renderer, string? pilotCallsign = null)
{
    private int _next;

    public IReadOnlyList<TranscriptEntry> TakeNew()
    {
        var lines = new List<TranscriptEntry>();
        var entries = session.Log.Entries;
        for (; _next < entries.Count; _next++)
        {
            var e = entries[_next];
            TranscriptEntry? line = e switch
            {
                AtcTransmissionLogged a => new(e.Time, TranscriptKind.Atc, a.Message.Unit.Callsign, a.Message.Unit.FrequencyKhz, renderer.Render(a.Message).Display),
                PilotIntentLogged p => new(e.Time, p.Unit is null ? TranscriptKind.Warning : TranscriptKind.Pilot, p.Unit?.Callsign, p.FrequencyKhz,
                    PilotCall(p.Intent) + (p.Unit is null ? " (nobody on this frequency)" : "")),
                PhaseChanged c => new(e.Time, TranscriptKind.Event, null, null, $"Phase: {PilotPhrase.Phase(c.To)}"),
                PhaseInferred i => new(e.Time, TranscriptKind.Event, null, null, $"Phase: {PilotPhrase.Phase(i.Phase)} ({i.Reason})"),
                ReadbackChecked { Verdict.IsCorrect: true } => null,
                ReadbackChecked r => new(e.Time, TranscriptKind.Warning, null, null,
                    "Readback: " + string.Join(", ", r.Verdict.Wrong.Select(w => $"{PilotPhrase.Item(w)} wrong").Concat(r.Verdict.Missing.Select(m => $"{PilotPhrase.Item(m)} missing")))),
                ReadbackMissed => new(e.Time, TranscriptKind.Warning, null, null, "Readback missed"),
                RequestRejected r => new(e.Time, TranscriptKind.Warning, null, null, $"Not now: {PilotPhrase.Of(r.Intent)} ({r.Reason})"),
                HandoffSkipped h => new(e.Time, TranscriptKind.Warning, null, null, $"Skipped {h.Expected.Callsign}, called {h.Called.Callsign}"),
                TaxiFallback t => new(e.Time, TranscriptKind.Event, null, null, $"No taxi route at {t.Icao}: {t.Reason}"),
                ConformanceWarning w => new(e.Time, TranscriptKind.Warning, null, null, $"{PilotPhrase.Check(w.Check)}{(w.Serious ? " (serious)" : "")}"),
                SessionEnded s => new(e.Time, TranscriptKind.Event, null, null, $"Flight ended: {s.Reason}"),
                _ => null,
            };
            if (line is not null) lines.Add(line);
        }
        return lines;
    }

    private string PilotCall(PilotIntent intent)
    {
        string phrase = PilotPhrase.Of(intent);
        if (phrase.Length > 0) phrase = char.ToUpperInvariant(phrase[0]) + phrase[1..];
        return pilotCallsign is null || intent is Mayday or PanPan ? phrase : $"{phrase}, {pilotCallsign}";
    }
}

/// <summary>Short English texts for pilot calls, readback items, phases and checks.</summary>
public static class PilotPhrase
{
    public static string Of(PilotIntent intent) => intent switch
    {
        Readback r when r.Items.Count == 0 => "Roger",
        Readback r => string.Join(", ", r.Items.Select(kv => Value(kv.Key, kv.Value))),
        RequestClearance { AtisLetter: { } a } => $"Request IFR clearance, information {a}",
        RequestClearance => "Request IFR clearance",
        RequestPushStart => "Request push and start",
        RequestTaxi => "Request taxi",
        ReadyForDeparture => "Ready for departure",
        RejectTakeoff => "Rejecting takeoff",
        CheckIn c => c.AltitudeFt is { } ft ? $"Checking in, passing {ft:N0} ft" + (c.ClearedAltitude is { } to ? $" for {Altitude(to)}" : "") : "Checking in",
        RequestLevel l => $"Request {Altitude(l.Level)}",
        RequestDirect d => $"Request direct {d.Fix}",
        RequestDescent => "Request descent",
        ReportEstablished => "Established",
        GoingAround => "Going around",
        ReportVacated => "Runway vacated",
        SayAgain => "Say again",
        Wilco => "Wilco",
        Unable u => u.Reason is { } why ? $"Unable, {why}" : "Unable",
        Mayday => "Mayday, mayday, mayday",
        PanPan => "Pan-pan, pan-pan, pan-pan",
        _ => intent.GetType().Name,
    };

    /// <summary>A readback value as a pilot would say it: "squawk 4774", "121.630", "FL120".</summary>
    public static string Value(ReadbackItem item, string value) => item switch
    {
        ReadbackItem.Frequency when int.TryParse(value, out int khz) => (khz / 1000.0).ToString("0.000", CultureInfo.InvariantCulture),
        ReadbackItem.Squawk => $"squawk {value}",
        ReadbackItem.Runway => $"runway {value}",
        ReadbackItem.HoldShorts => $"hold short {value.Replace(",", ", ")}",
        ReadbackItem.Heading => $"heading {value}",
        ReadbackItem.Speed => $"{value} knots",
        ReadbackItem.Qnh => $"QNH {value}",
        ReadbackItem.DirectTo => $"direct {value}",
        ReadbackItem.Stand => $"stand {value}",
        ReadbackItem.HoldingPoint => $"holding point {value}",
        ReadbackItem.Destination => $"cleared to {value}",
        ReadbackItem.Route => value == ReadbackValues.FlightPlannedRoute ? "flight planned route" : value,
        ReadbackItem.Clearance => value switch { "PUSH" => "push and start approved", "LINEUP" => "lining up", "TAKEOFF" => "cleared for takeoff", "LAND" => "cleared to land", "CROSS" => "crossing", _ => value },
        ReadbackItem.Facing => $"facing {value.ToLowerInvariant()}",
        ReadbackItem.InitialClimb or ReadbackItem.Altitude => value.EndsWith("FT") ? $"{value[..^2]} ft" : value,
        _ => value,
    };

    public static string Item(ReadbackItem item) => item switch
    {
        ReadbackItem.InitialClimb => "initial climb",
        ReadbackItem.HoldingPoint => "holding point",
        ReadbackItem.HoldShorts => "hold short",
        ReadbackItem.DirectTo => "direct to",
        ReadbackItem.Qnh => "QNH",
        _ => item.ToString().ToLowerInvariant(),
    };

    public static string Altitude(AltitudeTarget a) => a.Kind == AltitudeKind.FlightLevel ? $"FL{a.Value:000}" : $"{a.Value:N0} ft";

    public static string Phase(FlightPhase phase) => phase switch
    {
        FlightPhase.TaxiOut => "taxi out",
        FlightPhase.TakeoffRoll => "takeoff roll",
        FlightPhase.InitialClimb => "initial climb",
        FlightPhase.EnRoute => "en route",
        FlightPhase.LandingRoll => "landing roll",
        FlightPhase.TaxiIn => "taxi in",
        _ => phase.ToString().ToLowerInvariant(),
    };

    public static string Check(ConformanceCheck check) => check switch
    {
        ConformanceCheck.AltitudeBust => "Off the cleared altitude",
        ConformanceCheck.AltitudeNotComplying => "Not climbing or descending as cleared",
        ConformanceCheck.Heading => "Off the assigned heading",
        ConformanceCheck.OffRoute => "Off the cleared route",
        ConformanceCheck.Speed => "Not at the assigned speed",
        ConformanceCheck.Squawk => "Wrong squawk",
        ConformanceCheck.MissedHandoff => "Not on the new frequency",
        ConformanceCheck.RunwayIncursion => "Runway incursion",
        ConformanceCheck.TakeoffWithoutClearance => "Takeoff without clearance",
        ConformanceCheck.LandingWithoutClearance => "Landing without clearance",
        ConformanceCheck.TaxiOffRoute => "Off the cleared taxi route",
        _ => check.ToString(),
    };
}
