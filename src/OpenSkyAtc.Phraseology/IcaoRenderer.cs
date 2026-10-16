using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Messages;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Random;

namespace OpenSkyAtc.Phraseology;

public sealed record PhraseologyOptions
{
    /// <summary>
    /// Allow the European/UK (CAP 413) variants next to the design doc's ICAO wording. Off gives one fixed
    /// wording per part.
    /// </summary>
    public bool EuropeanVariants { get; init; } = true;

    /// <summary>"good day" / "bye bye" on every handoff. Not ICAO phraseology; on by owner's choice.</summary>
    public bool Courtesy { get; init; } = true;

    /// <summary>The unit's callsign after the aircraft's on its first transmission ("Vueling 1234, Barcelona Delivery, ...").</summary>
    public bool StationNameOnFirstContact { get; init; } = true;

    /// <summary>Say "niner" and "tree" for 9 and 3, for voices that pronounce plain digits badly.</summary>
    public bool NinerTree { get; init; }
}

/// <summary>A rendered message: what the UI shows and what the voice says.</summary>
public sealed record RenderedMessage(string Display, string Speech);

/// <summary>
/// ICAO/European renderer: turns an <see cref="AtcMessage"/> into display text and speech text with the
/// same wording. Stateful per session (first contact per unit, seeded wording choices), so create one per
/// flight and render every message in order.
/// </summary>
public sealed class IcaoRenderer
{
    private readonly PhraseologyOptions _o;
    private readonly IRandom _rng;
    private readonly CallsignTable _callsigns;
    private readonly Spoken _say;
    private readonly HashSet<ControllerUnit> _heardFrom = [];

    /// <param name="seed">Session seed; the renderer derives its own stream so wording never changes the brain's choices.</param>
    public IcaoRenderer(int seed, PhraseologyOptions? options = null, CallsignTable? callsigns = null)
    {
        _o = options ?? new PhraseologyOptions();
        _rng = new SeededRandom(unchecked(seed ^ 0x5EED1CA0));
        _callsigns = callsigns ?? CallsignTable.Empty;
        _say = new Spoken(_o.NinerTree);
    }

    private readonly record struct Text(string Display, string Speech, bool European = false)
    {
        public static Text Same(string s, bool european = false) => new(s, s, european);
        public static Text operator +(Text a, Text b) => new(a.Display + b.Display, a.Speech + b.Speech, a.European || b.European);
    }

    public RenderedMessage Render(AtcMessage message)
    {
        bool firstFromUnit = _heardFrom.Add(message.Unit);
        string? telephony = message.Callsign.Telephony ?? TelephonyFor(message.Callsign.Code);
        var (cd, cs) = CallsignFormat.Render(message.Callsign, telephony, abbreviated: !firstFromUnit, _say);

        var texts = new List<Text> { new(cd, cs) };
        if (_o.StationNameOnFirstContact && firstFromUnit) texts.Add(Text.Same(message.Unit.Callsign));

        var parts = message.Parts;
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i] is Wind wind && i + 1 < parts.Count && parts[i + 1] is ClearedTakeoff or ClearedLand)
            {
                texts.Add(WindAndClearance(wind, parts[++i]));
                continue;
            }
            texts.Add(Part(parts[i]));
        }
        return new RenderedMessage(string.Join(", ", texts.Select(t => t.Display)), string.Join(", ", texts.Select(t => t.Speech)));
    }

    /// <summary>Renders one part on its own, choosing a wording with the seeded RNG.</summary>
    public RenderedMessage RenderPart(AtcMessagePart part)
    {
        var t = Part(part);
        return new RenderedMessage(t.Display, t.Speech);
    }

    private string? TelephonyFor(string code) => code.Length > 3 ? _callsigns.Telephony(code[..3]) : null;

    private Text Part(AtcMessagePart part) => part switch
    {
        IfrClearance c => Text.Same("cleared to ") + Destination(c.Destination, c.DestinationName) + Text.Same(" via ") + Route(c.Sid)
                          + Text.Same(", ") + Climb(c.InitialClimb) + Text.Same(", ") + SquawkText(c.Squawk),
        ClearedTo c => Text.Same("cleared to ") + Destination(c.Destination, c.DestinationName),
        Via v => Text.Same("via ") + Route(v.Sid),
        ClimbTo c => Climb(c.Altitude),
        DescendTo d => Descend(d.Altitude),
        TurnHeading t => Heading(t),
        Direct d => Pick(
            new Text($"proceed direct {d.Fix}", $"proceed direct {_say.Designator(d.Fix)}"),
            new Text($"route direct {d.Fix}", $"route direct {_say.Designator(d.Fix)}", European: true)),
        Speed s => SpeedText(s),
        Qnh q => new Text($"QNH {q.Hpa}", $"QNH {_say.Qnh(q.Hpa)}"),
        Squawk s => SquawkText(s.Code),
        ReadbackCorrect => Pick(Text.Same("readback correct"), Text.Same("correct", european: true)),
        Contact c => ContactText(c),
        PushStartApproved => Pick(Text.Same("push and start approved"), Text.Same("pushback and start up approved", european: true)),
        TaxiVia t => Taxi(t),
        TaxiHoldingPoint h => new Text($"holding point {h.Name}", $"holding point {_say.Designator(h.Name)}"),
        RunwayIs r => RunwayText(r.Runway),
        HoldShort h => Text.Same("hold short ") + RunwayText(h.Runway),
        LineUpAndWait l => RunwayText(l.Runway) + Text.Same(", line up and wait"),
        Wind w => Text.Same("wind ") + WindValue(w),
        ClearedTakeoff t => RunwayText(t.Runway) + Text.Same(", cleared for takeoff"),
        ClearedLand l => RunwayText(l.Runway) + Text.Same(", cleared to land"),
        Negative { Repeated: true } => Text.Same("negative, I say again"),
        Negative => Text.Same("negative"),
        ReadBackRequest r => ReadBack(r.Items),
        StandBy => Text.Same("stand by"),
        SayAgainRequest => Pick(Text.Same("say again"), Text.Same("say again your request", european: true)),
        UnableContact u => Text.Same("unable, contact ") + UnitAndFrequency(u.Unit, shortName: false),
        Roger => Text.Same("roger"),
        EmergencyAcknowledged { Kind: EmergencyKind.Mayday } => Text.Same("roger mayday"),
        EmergencyAcknowledged => Text.Same("roger pan pan"),
        _ => throw new NotSupportedException($"No phraseology for {part.GetType().Name}."),
    };

    /// <summary>Wind before the clearance (design doc), or after it as "surface wind" (CAP 413 edition 24).</summary>
    private Text WindAndClearance(Wind wind, AtcMessagePart clearance)
    {
        var c = Part(clearance);
        return Pick(
            Text.Same("wind ") + WindValue(wind) + Text.Same(", ") + c,
            c + Text.Same(", surface wind ") + WindValue(wind) with { European = true });
    }

    private Text WindValue(Wind w) => w.SpeedKt == 0
        ? Text.Same("calm")
        : new Text($"{w.DirectionMagDeg:000}/{w.SpeedKt}", _say.Wind(w.DirectionMagDeg, w.SpeedKt));

    private Text Destination(string icao, string? name) => new(icao, name ?? _say.Spell(icao));

    private Text Route(string? sid) => sid is null
        ? Text.Same("flight planned route")
        : new Text($"{sid} departure", $"{_say.Designator(sid)} departure");

    private Text AltitudeText(AltitudeTarget alt) => new(Spoken.DisplayAltitude(alt), _say.Altitude(alt));

    /// <summary>Flight levels without "to" (CAP 413: "climb FL70"); altitudes optionally "to altitude".</summary>
    private Text Climb(AltitudeTarget alt) => VerticalText("climb", alt);
    private Text Descend(AltitudeTarget alt) => VerticalText("descend", alt);

    private Text VerticalText(string verb, AltitudeTarget alt) => alt.Kind == AltitudeKind.FlightLevel
        ? Text.Same($"{verb} ") + AltitudeText(alt)
        : Pick(Text.Same($"{verb} ") + AltitudeText(alt), Text.Same($"{verb} to altitude ") + AltitudeText(alt) with { European = true });

    private Text Heading(TurnHeading t)
    {
        string verb = t.Direction switch
        {
            TurnDirection.Left => "turn left heading",
            TurnDirection.Right => "turn right heading",
            _ => "fly heading",
        };
        return new Text($"{verb} {t.HeadingDeg:000}", $"{verb} {_say.Heading(t.HeadingDeg)}");
    }

    private Text SpeedText(Speed s)
    {
        var value = new Text($"{s.Knots} knots", $"{_say.Digits(s.Knots)} knots");
        return s.Action switch
        {
            // Design doc "reduce speed 250 knots"; CAP 413 "reduce speed to ...".
            SpeedAction.Reduce => Pick(Text.Same("reduce speed ") + value, Text.Same("reduce speed to ", european: true) + value),
            SpeedAction.Increase => Pick(Text.Same("increase speed ") + value, Text.Same("increase speed to ", european: true) + value),
            _ => Text.Same("maintain ") + value,
        };
    }

    private Text SquawkText(int code) => new($"squawk {code:0000}", $"squawk {_say.Squawk(code)}");

    private Text RunwayText(string rwy) => new($"runway {rwy}", $"runway {_say.Runway(rwy)}");

    private Text ContactText(Contact c)
    {
        var target = Pick(UnitAndFrequency(c.Unit, shortName: false), UnitAndFrequency(c.Unit, shortName: true) with { European = true });
        var text = c.Condition == ContactCondition.WhenReadyForPushAndStart
            ? Pick(
                Text.Same("when ready for push and start contact ") + target,
                Text.Same("for push and start contact ", european: true) + target,
                Text.Same("contact ", european: true) + target)
            : Text.Same("contact ") + target;
        if (_o.Courtesy) text += Pick(Text.Same(", good day"), Text.Same(", bye bye", european: true));
        return text;
    }

    private Text UnitAndFrequency(ControllerUnit unit, bool shortName)
    {
        string name = shortName && ShortName(unit.Role) is { } s ? s : unit.Callsign;
        return new Text($"{name} {Spoken.DisplayFrequency(unit.FrequencyKhz)}", $"{name} {_say.Frequency(unit.FrequencyKhz)}");
    }

    private static string? ShortName(ControllerRole role) => role switch
    {
        ControllerRole.Delivery => "Delivery",
        ControllerRole.Ground => "Ground",
        ControllerRole.Tower => "Tower",
        ControllerRole.Departure => "Departure",
        ControllerRole.Approach => "Approach",
        _ => null, // area control keeps its full name
    };

    private Text Taxi(TaxiVia t)
    {
        var runway = RunwayText(t.Runway);
        var point = t.HoldingPoint is { } hp
            ? new Text($"holding point {hp} ", $"holding point {_say.Designator(hp)} ") + runway
            : Text.Same("holding point ") + runway;
        var via = t.Segments.Count > 0
            ? new Text($" via {string.Join(", ", t.Segments)}", $" via {string.Join(", ", t.Segments.Select(_say.Designator))}")
            : Text.Same("");
        var holds = Text.Same("");
        foreach (var h in t.HoldShorts) holds += Text.Same(", hold short ") + RunwayText(h);

        // "taxi to holding point ..." (design doc) or "taxi holding point ..." (CAP 413).
        return Pick(Text.Same("taxi to ") + point, Text.Same("taxi ") + point with { European = true }) + via + holds;
    }

    private Text ReadBack(IReadOnlyList<ReadbackItem> items)
    {
        string list = string.Join(" and ", items.Select(ItemName));
        return Pick(Text.Same($"read back the {list}"), Text.Same($"read back {list}", european: true));
    }

    public static string ItemName(ReadbackItem item) => item switch
    {
        ReadbackItem.InitialClimb => "initial climb",
        ReadbackItem.HoldingPoint => "holding point",
        ReadbackItem.HoldShorts => "hold short",
        ReadbackItem.DirectTo => "direct",
        ReadbackItem.Qnh => "QNH",
        _ => item.ToString().ToLowerInvariant(),
    };

    /// <summary>Chooses one of the enabled wordings. European variants are skipped when they are turned off.</summary>
    private Text Pick(params Text[] variants)
    {
        var enabled = _o.EuropeanVariants ? variants : variants.Where(v => !v.European).ToArray();
        if (enabled.Length == 0) enabled = [variants[0]];
        var chosen = enabled.Length == 1 ? enabled[0] : enabled[_rng.Next(0, enabled.Length)];
        return chosen with { European = false };
    }
}
