using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenSkyAtc.Core.Conversation;

/// <summary>
/// Turns what a pilot types (hard-mode readback form) or says (speech, step 14) into the canonical
/// <see cref="ReadbackValues"/> form the verifier compares: "121.63" -> "121630", "FL 120" -> "FL120",
/// "5000 ft" -> "5000FT", "rwy 36l" -> "36L". Text it cannot make sense of is returned normalised, so the
/// readback is then judged wrong rather than silently fixed.
/// </summary>
public static partial class ReadbackInput
{
    /// <summary>The choices for <see cref="ReadbackItem.Clearance"/>; the form shows them as a list.</summary>
    public static readonly IReadOnlyList<string> Clearances = ["PUSH", "LINEUP", "TAKEOFF", "LAND", "CROSS"];

    /// <summary>The choices for <see cref="ReadbackItem.Facing"/>.</summary>
    public static readonly IReadOnlyList<string> Directions = ["NORTH", "NORTHEAST", "EAST", "SOUTHEAST", "SOUTH", "SOUTHWEST", "WEST", "NORTHWEST"];

    public static string Normalize(ReadbackItem item, string? text)
    {
        string t = (text ?? "").Trim().ToUpperInvariant();
        if (t.Length == 0) return "";
        return item switch
        {
            ReadbackItem.Frequency => Frequency(t),
            ReadbackItem.InitialClimb or ReadbackItem.Altitude => Altitude(t),
            ReadbackItem.Squawk => Digits(t) is { Length: 4 } d ? d : t,
            ReadbackItem.Heading => int.TryParse(Digits(t), out int h) && h is >= 0 and <= 360 ? ReadbackValues.Heading(h == 0 ? 360 : h) : t,
            ReadbackItem.Speed or ReadbackItem.Qnh => int.TryParse(Digits(t), out int n) ? n.ToString(CultureInfo.InvariantCulture) : t,
            ReadbackItem.Runway => Runway(t),
            ReadbackItem.HoldShorts => string.Join(",", t.Split([',', ' ', ';', '/'], StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w is not ("AND" or "RWY" or "RUNWAY" or "RUNWAYS")).Select(Runway)),
            ReadbackItem.Route => t is "FPL" or "FLIGHT PLAN" or "FLIGHT PLANNED" or "AS FILED" ? ReadbackValues.FlightPlannedRoute : Squash(t),
            ReadbackItem.HoldingPoint or ReadbackItem.Stand or ReadbackItem.DirectTo or ReadbackItem.Destination => Squash(Strip(t, "HOLDING POINT", "STAND", "GATE", "DIRECT", "TO")),
            ReadbackItem.Clearance => Clearance(t),
            ReadbackItem.Facing => Squash(Strip(t, "FACING")) switch
            {
                "N" => "NORTH", "NE" => "NORTHEAST", "E" => "EAST", "SE" => "SOUTHEAST",
                "S" => "SOUTH", "SW" => "SOUTHWEST", "W" => "WEST", "NW" => "NORTHWEST", var d => d,
            },
            _ => t,
        };
    }

    /// <summary>"121.63", "121,630", "121630", "121.6" -> kHz ("121630", "121600").</summary>
    private static string Frequency(string t)
    {
        string s = t.Replace(',', '.').Replace(" ", "");
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
        {
            if (v is >= 118 and < 137) return ((int)Math.Round(v * 1000)).ToString(CultureInfo.InvariantCulture);
            if (v is >= 118_000 and < 137_000) return ((int)v).ToString(CultureInfo.InvariantCulture);
        }
        return t;
    }

    /// <summary>"FL120", "FL 120", "flight level 120" -> "FL120"; "5000", "5000 ft", "5,000 feet" -> "5000FT".</summary>
    private static string Altitude(string t)
    {
        bool level = t.StartsWith("FL") || t.StartsWith("FLIGHT LEVEL") || t.StartsWith("LEVEL");
        if (!int.TryParse(Digits(t), out int n)) return t;
        return level ? $"FL{n:000}" : $"{n}FT";
    }

    /// <summary>"rwy 36l", "runway 3 6 left", "9" -> "36L", "09".</summary>
    private static string Runway(string t)
    {
        string s = Strip(t, "RUNWAY", "RWY");
        s = s.Replace("LEFT", "L").Replace("RIGHT", "R").Replace("CENTER", "C").Replace("CENTRE", "C").Replace(" ", "");
        var m = RunwayPattern().Match(s);
        if (!m.Success) return s;
        int n = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        return n is >= 1 and <= 36 ? $"{n:00}{m.Groups[2].Value}" : s;
    }

    private static string Clearance(string t) => Squash(t) switch
    {
        "LINEUP" or "LINEUPANDWAIT" or "LUAW" => "LINEUP",
        "TAKEOFF" or "CLEAREDFORTAKEOFF" or "CLEAREDTAKEOFF" => "TAKEOFF",
        "LAND" or "LANDING" or "CLEAREDTOLAND" => "LAND",
        "CROSS" or "CROSSING" or "CLEAREDTOCROSS" => "CROSS",
        "PUSH" or "PUSHBACK" or "PUSHANDSTART" or "PUSHANDSTARTAPPROVED" or "PUSHBACKAPPROVED" => "PUSH",
        var other => other,
    };

    private static string Digits(string t) => new(t.Where(char.IsAsciiDigit).ToArray());

    private static string Squash(string t) => new(t.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());

    private static string Strip(string t, params string[] words)
    {
        foreach (var w in words)
            if (t.StartsWith(w + " ", StringComparison.Ordinal)) t = t[(w.Length + 1)..];
        return t.Trim();
    }

    [GeneratedRegex(@"^(\d{1,2})([LRC]?)$")]
    private static partial Regex RunwayPattern();
}
