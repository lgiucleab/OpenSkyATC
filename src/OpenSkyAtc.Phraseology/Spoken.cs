using System.Globalization;
using System.Text.RegularExpressions;
using OpenSkyAtc.Core.Model;

namespace OpenSkyAtc.Phraseology;

/// <summary>
/// ICAO number and spelling rules for speech text (design doc "Number rules"), plus the matching display
/// formats. Digits are spoken one by one; "niner" and "tree" only when <see cref="NinerTree"/> is set.
/// </summary>
public sealed partial class Spoken(bool ninerTree = false)
{
    private static readonly string[] Phonetic =
    [
        "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel", "India", "Juliett", "Kilo", "Lima", "Mike",
        "November", "Oscar", "Papa", "Quebec", "Romeo", "Sierra", "Tango", "Uniform", "Victor", "Whiskey", "X-ray", "Yankee", "Zulu",
    ];

    public bool NinerTree { get; } = ninerTree;

    public string Digit(char d) => d switch
    {
        '0' => "zero",
        '1' => "one",
        '2' => "two",
        '3' => NinerTree ? "tree" : "three",
        '4' => "four",
        '5' => "five",
        '6' => "six",
        '7' => "seven",
        '8' => "eight",
        '9' => NinerTree ? "niner" : "nine",
        _ => throw new ArgumentOutOfRangeException(nameof(d), d, "Not a digit."),
    };

    /// <summary>"4521" -> "four five two one".</summary>
    public string Digits(string digits) => string.Join(" ", digits.Where(char.IsDigit).Select(Digit));

    public string Digits(int value, int minWidth = 1) => Digits(value.ToString(new string('0', minWidth), CultureInfo.InvariantCulture));

    public static string Letter(char c) => char.IsAsciiLetter(c) ? Phonetic[char.ToUpperInvariant(c) - 'A'] : c.ToString();

    /// <summary>Letters as phonetic words and digits one by one: "A1" -> "Alpha one", "LEMD" -> "Lima Echo Mike Delta".</summary>
    public string Spell(string text) =>
        string.Join(" ", text.Where(char.IsAsciiLetterOrDigit).Select(c => char.IsDigit(c) ? Digit(c) : Letter(c)));

    /// <summary>
    /// FL120 -> "flight level one two zero"; FL70 -> "flight level seven zero"; FL100 -> "flight level one hundred"
    /// (CAP 413 2.13: whole hundreds use HUNDRED). 5000 ft -> "five thousand feet"; 3500 ft -> "three thousand five hundred feet".
    /// </summary>
    public string Altitude(AltitudeTarget alt) => alt.Kind == AltitudeKind.FlightLevel
        ? alt.Value > 0 && alt.Value % 100 == 0
            ? $"flight level {Digits(alt.Value / 100)} hundred"
            : $"flight level {Digits(alt.Value)}"
        : $"{Feet(alt.Value)} feet";

    /// <summary>ICAO altitude words: thousands as digits plus "thousand", then hundreds; other values digit by digit.</summary>
    public string Feet(int feet)
    {
        if (feet <= 0 || feet % 100 != 0) return Digits(feet);
        int thousands = feet / 1000, hundreds = feet % 1000 / 100;
        var words = new List<string>();
        if (thousands > 0) words.Add($"{Digits(thousands)} thousand");
        if (hundreds > 0) words.Add($"{Digit((char)('0' + hundreds))} hundred");
        return string.Join(" ", words);
    }

    /// <summary>090 -> "zero nine zero".</summary>
    public string Heading(int degrees) => Digits(degrees, 3);

    /// <summary>
    /// 121.850 -> "one two one decimal eight five zero". When both final digits are zero only the first
    /// four digits are spoken: 118.100 -> "one one eight decimal one" (ICAO six-digit frequency rule).
    /// </summary>
    public string Frequency(int khz)
    {
        string s = DisplayFrequency(khz);           // "118.100"
        string[] parts = s.Split('.');
        string decimals = parts[1].EndsWith("00", StringComparison.Ordinal) ? parts[1][..1] : parts[1];
        return $"{Digits(parts[0])} decimal {Digits(decimals)}";
    }

    public string Squawk(int code) => Digits(code, 4);

    public string Qnh(int hpa) => Digits(hpa, 3);

    /// <summary>"24L" -> "two four left"; "06" -> "zero six".</summary>
    public string Runway(string designator)
    {
        var m = RunwayPattern().Match(designator.Trim().ToUpperInvariant());
        if (!m.Success) return Spell(designator);
        string side = m.Groups[2].Value switch
        {
            "L" => " left",
            "R" => " right",
            "C" => " centre",
            _ => "",
        };
        return Digits(m.Groups[1].Value) + side;
    }

    /// <summary>Wind direction and speed: "two five zero degrees, one two knots".</summary>
    public string Wind(int directionDeg, int knots) => $"{Digits(directionDeg, 3)} degrees, {Digits(knots)} knots";

    /// <summary>
    /// SID/STAR designators: "NAPSI1C" -> "Napsi one Charlie". Fixes of four or more letters are spoken as a
    /// word; shorter idents and taxiway names are spelled ("BCN" -> "Bravo Charlie November", "A1" -> "Alpha one").
    /// </summary>
    public string Designator(string text)
    {
        var m = ProcedurePattern().Match(text.Trim().ToUpperInvariant());
        if (m.Success) return $"{Word(m.Groups[1].Value)} {Digit(m.Groups[2].Value[0])} {Letter(m.Groups[3].Value[0])}";
        string t = text.Trim();
        return t.Length >= 4 && t.All(char.IsAsciiLetter) ? Word(t) : Spell(t);
    }

    public static string DisplayFrequency(int khz) => (khz / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);

    public static string DisplayAltitude(AltitudeTarget alt) => alt.Kind == AltitudeKind.FlightLevel
        ? $"FL{alt.Value.ToString(CultureInfo.InvariantCulture)}"
        : $"{alt.Value.ToString(CultureInfo.InvariantCulture)} ft";

    private static string Word(string upper) => upper.Length == 0 ? upper : upper[0] + upper[1..].ToLowerInvariant();

    [GeneratedRegex(@"^(\d{1,2})([LRC]?)$")]
    private static partial Regex RunwayPattern();

    [GeneratedRegex(@"^([A-Z]{3,})(\d)([A-Z])$")]
    private static partial Regex ProcedurePattern();
}
