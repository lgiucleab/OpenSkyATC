using System.Text.RegularExpressions;
using OpenSkyAtc.Core.Messages;

namespace OpenSkyAtc.Phraseology;

/// <summary>
/// ICAO airline designator to radio telephony ("IBE" -> "Iberia"). The shipped table is
/// data/airline-callsigns.csv, generated from Wikidata (CC0) by tools/airline-callsigns/generate.py.
/// </summary>
public sealed class CallsignTable
{
    private readonly Dictionary<string, string> _telephony;

    private CallsignTable(Dictionary<string, string> telephony) => _telephony = telephony;

    public static readonly CallsignTable Empty = new([]);

    public int Count => _telephony.Count;

    public string? Telephony(string icaoDesignator) => _telephony.GetValueOrDefault(icaoDesignator.ToUpperInvariant());

    public static CallsignTable Load(string path) => Parse(File.ReadLines(path));

    /// <summary>Parses "ICAO,TELEPHONY" lines; blank lines and lines starting with # are skipped. Telephony is title-cased.</summary>
    public static CallsignTable Parse(IEnumerable<string> lines)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int lineNo = 0;
        foreach (var raw in lines)
        {
            lineNo++;
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split(',', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0].Length != 3 || parts[1].Length == 0)
                throw new FormatException($"Callsign table line {lineNo}: expected 'ICAO,TELEPHONY', got '{line}'.");
            map[parts[0]] = TitleCase(parts[1]);
        }
        return new CallsignTable(map);
    }

    /// <summary>"WIZZ AIR" -> "Wizz Air". Words without a vowel are acronyms and stay upper case ("KLM").</summary>
    private static string TitleCase(string s) =>
        string.Join(" ", s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToUpperInvariant().IndexOfAny(['A', 'E', 'I', 'O', 'U', 'Y']) < 0 ? w.ToUpperInvariant() : w[0] + w[1..].ToLowerInvariant()));
}

/// <summary>How the aircraft's callsign is written and spoken.</summary>
public static partial class CallsignFormat
{
    public enum Kind
    {
        /// <summary>Airline designator + flight number, telephony known: "Vueling 1234".</summary>
        Airline,

        /// <summary>Airline designator with no telephony: spelled, "Victor Lima Golf 1234".</summary>
        UnknownAirline,

        /// <summary>Registration: "EC-ABC", abbreviated to "E-BC" once ATC chooses to.</summary>
        Registration,
    }

    public static Kind Classify(AircraftCallsign callsign, string? telephony) =>
        AirlinePattern().IsMatch(Clean(callsign.Code))
            ? telephony is null ? Kind.UnknownAirline : Kind.Airline
            : Kind.Registration;

    /// <param name="abbreviated">Registrations only: first character plus the last two (ICAO abbreviation).</param>
    public static (string Display, string Speech) Render(AircraftCallsign callsign, string? telephony, bool abbreviated, Spoken spoken)
    {
        string code = Clean(callsign.Code);
        switch (Classify(callsign, telephony))
        {
            case Kind.Airline:
            {
                string flight = code[3..];
                return ($"{telephony} {flight}", $"{telephony} {spoken.Spell(flight)}");
            }
            case Kind.UnknownAirline:
                return (code, $"{spoken.Spell(code[..3])} {spoken.Spell(code[3..])}");
            default:
                string reg = callsign.Code.Trim().ToUpperInvariant();
                if (!abbreviated || code.Length < 4) return (reg, spoken.Spell(code));
                string shortCode = code[..1] + code[^2..];
                return ($"{code[..1]}-{code[^2..]}", spoken.Spell(shortCode));
        }
    }

    private static string Clean(string code) => new string(code.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();

    [GeneratedRegex(@"^[A-Z]{3}\d[A-Z0-9]*$")]
    private static partial Regex AirlinePattern();
}
