using System.Globalization;
using System.Text.RegularExpressions;
using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Model;

namespace OpenSkyAtc.Voice.Input;

/// <summary>What the parser knows about the moment of the call.</summary>
/// <param name="Available">The brain's <c>AvailableIntents</c>: the requests that make sense now, and the readback
/// ATC is waiting for (with its expected values).</param>
/// <param name="Aliases">Spoken names for canonical values, for example "BARCELONA" for "LEBL".</param>
public sealed record ParseContext(IReadOnlyList<PilotIntent> Available, IReadOnlyDictionary<string, string>? Aliases = null)
{
    public Readback? PendingReadback => Available.OfType<Readback>().FirstOrDefault();
}

/// <param name="Intent">What the pilot said, or null when the call made no sense (ATC answers "say again").</param>
/// <param name="Normalized">The transcript in <see cref="SpokenText"/> form.</param>
public sealed record ParsedCall(PilotIntent? Intent, string Normalized);

/// <summary>
/// Maps a transcript to a <see cref="PilotIntent"/> with its slots (step 14). Requests are found by their key words;
/// a readback is read item by item against what ATC is waiting for: each item is looked for where a pilot says it
/// ("squawk 4721", "flight level 120", "runway 36 left"), and a name (SID, holding point, fix) counts as read back
/// when the transcript contains it. A value read back differently is kept as heard, so ATC corrects it; nothing is
/// guessed. A call that matches nothing gives a null intent.
/// </summary>
public static partial class IntentParser
{
    public static ParsedCall Parse(string transcript, ParseContext context)
    {
        string n = SpokenText.Normalize(transcript);
        return new ParsedCall(n.Length == 0 ? null : Classify(n, context), n);
    }

    private static PilotIntent? Classify(string n, ParseContext ctx)
    {
        if (Has(n, "MAYDAY")) return new Mayday();
        if (Has(n, "PAN PAN")) return new PanPan();
        if (Has(n, "SAY AGAIN")) return new SayAgain();

        var request = Request(n, ctx);
        if (request is not null && IsAvailable(request, ctx)) return request;

        if (ctx.PendingReadback is { } pending)
        {
            var items = ReadItems(n, pending, ctx);
            if (items.Count > 0) return new Readback(items, pending.ExchangeId);
        }
        if (request is not null) return request;
        if (Has(n, "UNABLE")) return new Unable(Rest(n, "UNABLE"));
        if (Has(n, "WILCO") || Has(n, "ROGER") || Has(n, "COPIED")) return new Wilco();
        return null;
    }

    private static bool IsAvailable(PilotIntent intent, ParseContext ctx) => ctx.Available.Any(a => a.GetType() == intent.GetType());

    // ---- requests and reports -------------------------------------------------------------------------------

    private static PilotIntent? Request(string n, ParseContext ctx)
    {
        bool asks = Has(n, "REQUEST") || Has(n, "REQUESTING");
        if (Has(n, "STOPPING") || Has(n, "REJECTING") || Has(n, "ABORTING")) return new RejectTakeoff();
        if (Has(n, "GOING AROUND")) return new GoingAround();
        if (Has(n, "VACATED") || Has(n, "CLEAR OF THE RUNWAY") || Has(n, "CLEAR OF RUNWAY")) return new ReportVacated();
        if (Has(n, "ESTABLISHED")) return new ReportEstablished();

        if (asks && Has(n, "DIRECT") && Match(n, DirectFix()) is { } fix) return new RequestDirect(fix);
        if (asks && (Has(n, "CLIMB") || Has(n, "DESCENT") || Has(n, "DESCEND") || Has(n, "FLIGHT LEVEL") || Has(n, "FL") || Has(n, "FEET"))
            && Altitudes(n).Cast<AltitudeTarget?>().FirstOrDefault() is { } level)
            return new RequestLevel(level);
        if (asks && (Has(n, "DESCENT") || Has(n, "DESCEND"))) return new RequestDescent();

        if (Pushes(n) && (asks || Has(n, "READY"))) return new RequestPushStart();
        if ((Has(n, "START UP") || Has(n, "STARTUP")) && asks && !Has(n, "CLEARANCE")) return new RequestPushStart();
        if (Has(n, "TAXI") && (asks || Has(n, "READY"))) return new RequestTaxi();
        if (Has(n, "READY TO COPY") || (Has(n, "CLEARANCE") && (asks || Has(n, "INFORMATION"))) || (asks && Has(n, "IFR")))
            return new RequestClearance(Match(n, AtisLetter()));
        if (Has(n, "READY") && !Has(n, "COPY")) return new ReadyForDeparture();

        bool greets = Has(n, "PASSING") || Has(n, "WITH YOU") || Has(n, "CHECKING IN") || Has(n, "GOOD MORNING")
                      || Has(n, "GOOD AFTERNOON") || Has(n, "GOOD EVENING") || Has(n, "GOOD DAY") || Has(n, "HELLO");
        bool levelReport = Has(n, "CLIMBING") || Has(n, "DESCENDING") || Has(n, "MAINTAINING") || Has(n, "LEVEL");
        if (greets || (levelReport && ctx.PendingReadback is null)) return CheckIn(n);
        return null;
    }

    /// <summary>"passing 4500 feet climbing flight level 120": passing altitude and cleared level, both optional.</summary>
    private static CheckIn CheckIn(string n)
    {
        int? passing = null;
        AltitudeTarget? cleared = null;
        int at = n.IndexOf("PASSING", StringComparison.Ordinal);
        var heard = Altitudes(at >= 0 ? n[(at + 7)..] : n).ToList();
        if (at >= 0 && heard.Count > 0)
        {
            passing = heard[0].NominalFeet;
            if (heard.Count > 1) cleared = heard[1];
        }
        else if (heard.Count > 0) cleared = heard[0];
        return new CheckIn(passing, cleared);
    }

    // ---- readbacks ------------------------------------------------------------------------------------------

    private static Dictionary<ReadbackItem, string> ReadItems(string n, Readback pending, ParseContext ctx)
    {
        var heard = new Dictionary<ReadbackItem, string>();
        foreach (var (item, expected) in pending.Items)
            if (Read(item, n, expected, ctx) is { } value) heard[item] = value;
        return heard;
    }

    private static string? Read(ReadbackItem item, string n, string expected, ParseContext ctx) => item switch
    {
        ReadbackItem.Frequency => Pick(Frequencies(n), expected),
        ReadbackItem.Squawk => Pick(Squawks(n), expected),
        ReadbackItem.Altitude or ReadbackItem.InitialClimb => Pick(Altitudes(n).Select(ReadbackValues.Altitude), expected),
        ReadbackItem.Heading => Pick(All(n, HeadingValue()).Select(h => ReadbackInput.Normalize(ReadbackItem.Heading, h)), expected),
        ReadbackItem.Speed => Pick(All(n, SpeedValue()).Select(s => ReadbackInput.Normalize(ReadbackItem.Speed, s)), expected),
        ReadbackItem.Qnh => Pick(All(n, QnhValue()), expected),
        ReadbackItem.Runway => Pick(Runways(n, holdShort: false), expected),
        ReadbackItem.HoldShorts => Runways(n, holdShort: true) is { Count: > 0 } r ? string.Join(",", r) : null,
        ReadbackItem.Clearance => Pick(Clearances(n), expected),
        ReadbackItem.Facing => Pick(All(n, FacingValue()).Select(f => ReadbackInput.Normalize(ReadbackItem.Facing, f)), expected),
        ReadbackItem.Route => Route(n, expected),
        ReadbackItem.Destination => Name(n, expected, ctx, Match(n, ClearedTo())),
        ReadbackItem.HoldingPoint => Name(n, expected, ctx, Match(n, HoldingPointName())),
        ReadbackItem.Stand => Name(n, expected, ctx, Match(n, StandName())),
        ReadbackItem.DirectTo => Name(n, expected, ctx, Match(n, DirectFix())),
        _ => null,
    };

    /// <summary>The value that matches what ATC said, else the first one heard (read back wrongly), else null.</summary>
    private static string? Pick(IEnumerable<string> candidates, string expected)
    {
        var list = candidates.ToList();
        return list.FirstOrDefault(c => ReadbackValues.Normalize(c) == ReadbackValues.Normalize(expected)) ?? list.FirstOrDefault();
    }

    /// <summary>A name counts as read back when the transcript contains it (or a spoken alias); else what follows its key word.</summary>
    private static string? Name(string n, string expected, ParseContext ctx, string? said)
    {
        string squashed = SpokenText.Squash(n);
        if (squashed.Contains(SpokenText.Squash(expected), StringComparison.Ordinal)) return expected;
        if (ctx.Aliases is { } aliases && aliases.Any(a => a.Value == expected && squashed.Contains(SpokenText.Squash(a.Key.ToUpperInvariant()), StringComparison.Ordinal)))
            return expected;
        return said is null ? null : SpokenText.Squash(said);
    }

    private static string? Route(string n, string expected)
    {
        bool planned = Has(n, "FLIGHT PLAN") || Has(n, "PLANNED ROUTE") || Has(n, "AS FILED");
        if (expected == ReadbackValues.FlightPlannedRoute)
            return planned ? expected : Match(n, SidName()) is { } other ? SpokenText.Squash(other) : null;
        if (SpokenText.Squash(n).Contains(SpokenText.Squash(expected), StringComparison.Ordinal)) return expected;
        if (Match(n, SidName()) is { } sid) return SpokenText.Squash(sid);
        return planned ? ReadbackValues.FlightPlannedRoute : null;
    }

    private static IEnumerable<string> Frequencies(string n)
    {
        foreach (Match m in Number().Matches(n))
        {
            string normalized = ReadbackInput.Normalize(ReadbackItem.Frequency, m.Value);
            if (normalized.Length == 6 && normalized.All(char.IsAsciiDigit)) yield return normalized;
        }
    }

    /// <summary>Four octal digits, after "squawk" or on their own.</summary>
    private static IEnumerable<string> Squawks(string n)
    {
        var keyed = All(n, SquawkValue()).ToList();
        if (keyed.Count > 0) return keyed;
        return Number().Matches(n).Select(m => m.Value).Where(v => v.Length == 4 && v.All(c => c is >= '0' and <= '7'));
    }

    private static IEnumerable<AltitudeTarget> Altitudes(string n)
    {
        var found = new List<(int At, AltitudeTarget Value)>();
        foreach (Match m in FlightLevel().Matches(n))
            found.Add((m.Index, AltitudeTarget.FlightLevel(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))));
        foreach (Match m in Feet().Matches(n))
            found.Add((m.Index, AltitudeTarget.Feet(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))));
        foreach (Match m in AltitudeAfterVerb().Matches(n))
            if (!found.Any(f => Math.Abs(f.At - m.Groups[1].Index) < 3))
                found.Add((m.Groups[1].Index, AltitudeTarget.Feet(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))));
        return found.OrderBy(f => f.At).Select(f => f.Value);
    }

    /// <summary>Runways said with "runway": those after "hold short" when <paramref name="holdShort"/>, the others otherwise.</summary>
    private static List<string> Runways(string n, bool holdShort)
    {
        var result = new List<string>();
        int shortAt = n.IndexOf("SHORT", StringComparison.Ordinal);
        foreach (Match m in RunwayValue().Matches(n))
        {
            bool inHoldShort = shortAt >= 0 && m.Index > shortAt && !Regex.IsMatch(n[shortAt..m.Index], @"\b(CLEARED|LINE|LINING|TAXI|VIA|HOLDING POINT)\b");
            if (inHoldShort != holdShort) continue;
            string value = ReadbackInput.Normalize(ReadbackItem.Runway, m.Groups[1].Value + (m.Groups[2].Success ? " " + m.Groups[2].Value : ""));
            if (!result.Contains(value)) result.Add(value);
        }
        return result;
    }

    private static IEnumerable<string> Clearances(string n)
    {
        if (Pushes(n)) yield return ReadbackValues.Push;
        if (Has(n, "LINE UP") || Has(n, "LINING UP")) yield return ReadbackValues.LineUp;
        if (Has(n, "TAKE OFF") || Has(n, "TAKEOFF")) yield return ReadbackValues.Takeoff;
        if (Has(n, "LAND")) yield return ReadbackValues.Land;
        if (Has(n, "CROSS") || Has(n, "CROSSING")) yield return ReadbackValues.Cross;
    }

    // ---- helpers --------------------------------------------------------------------------------------------

    private static bool Has(string n, string phrase) =>
        Regex.IsMatch(n, $@"(^| ){Regex.Escape(phrase)}( |$)");

    /// <summary>"push", "pushback", "pushing back".</summary>
    private static bool Pushes(string n) => Regex.IsMatch(n, @"(^| )PUSH(BACK|ING)?( |$)");

    private static string? Match(string n, Regex r) => r.Match(n) is { Success: true } m ? m.Groups[1].Value : null;

    private static IEnumerable<string> All(string n, Regex r) => r.Matches(n).Select(m => m.Groups[1].Value);

    private static string? Rest(string n, string word)
    {
        int at = n.IndexOf(word, StringComparison.Ordinal);
        string rest = n[(at + word.Length)..].Trim();
        return rest.Length == 0 ? null : rest.ToLowerInvariant();
    }

    [GeneratedRegex(@"\b(\d+(?:\.\d+)?)\b")]
    private static partial Regex Number();

    [GeneratedRegex(@"\bSQUAWK(?:ING)? (\d{4})\b")]
    private static partial Regex SquawkValue();

    [GeneratedRegex(@"\b(?:FLIGHT LEVEL|FL|LEVEL) (\d{2,3})\b")]
    private static partial Regex FlightLevel();

    [GeneratedRegex(@"\b(\d{3,5}) (?:FEET|FT)\b")]
    private static partial Regex Feet();

    [GeneratedRegex(@"\b(?:CLIMB|CLIMBING|DESCEND|DESCENDING|MAINTAIN|MAINTAINING|ALTITUDE)(?: AND MAINTAIN| TO| ALTITUDE)* (\d{3,5})\b")]
    private static partial Regex AltitudeAfterVerb();

    [GeneratedRegex(@"\bHEADING (\d{1,3})\b")]
    private static partial Regex HeadingValue();

    [GeneratedRegex(@"\b(?:SPEED |(?=\d{2,3} (?:KNOTS|KTS)))(\d{2,3})\b")]
    private static partial Regex SpeedValue();

    [GeneratedRegex(@"\bQNH (\d{3,4})\b")]
    private static partial Regex QnhValue();

    [GeneratedRegex(@"\bRUNWAY (\d{1,2})(?: (LEFT|RIGHT|CENTER|CENTRE|L|R|C))?\b")]
    private static partial Regex RunwayValue();

    [GeneratedRegex(@"\bFACING (NORTH ?EAST|NORTH ?WEST|SOUTH ?EAST|SOUTH ?WEST|NORTH|EAST|SOUTH|WEST)\b")]
    private static partial Regex FacingValue();

    [GeneratedRegex(@"\bDIRECT(?: TO)? ([A-Z0-9]{2,})\b")]
    private static partial Regex DirectFix();

    [GeneratedRegex(@"\bCLEARED TO ([A-Z]{3,})\b")]
    private static partial Regex ClearedTo();

    [GeneratedRegex(@"\bHOLDING POINT ([A-Z]{1,2}(?: ?\d{1,2})?)\b")]
    private static partial Regex HoldingPointName();

    [GeneratedRegex(@"\b(?:STAND|GATE) ([A-Z]?(?: ?\d{1,3})(?: ?[A-Z])?)\b")]
    private static partial Regex StandName();

    [GeneratedRegex(@"\b([A-Z]{3,6} \d(?: [A-Z])?) DEPARTURE\b")]
    private static partial Regex SidName();

    [GeneratedRegex(@"\bINFORMATION ([A-Z])\b")]
    private static partial Regex AtisLetter();
}
