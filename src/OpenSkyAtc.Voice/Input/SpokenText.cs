using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenSkyAtc.Voice.Input;

/// <summary>
/// Turns a Whisper transcript into one canonical form for the intent parser: upper case words separated by single
/// spaces, numbers as digits ("one two one decimal six three five" and "121.635" both give "121.635", "niner" is 9,
/// "three thousand five hundred" is 3500), ICAO spelling as letters ("tango oscar bravo echo kilo" gives "TOBEK"),
/// letters and digits split apart ("FL120" gives "FL 120", "36L" gives "36 L").
/// </summary>
public static partial class SpokenText
{
    private enum Kind { Word, Digits, Dot, Letter }

    private readonly record struct Tok(string Text, Kind Kind, bool FromWord = false);

    private static readonly Dictionary<string, int> DigitWords = new()
    {
        ["ZERO"] = 0, ["ONE"] = 1, ["TWO"] = 2, ["THREE"] = 3, ["TREE"] = 3, ["FOUR"] = 4, ["FOWER"] = 4,
        ["FIVE"] = 5, ["FIFE"] = 5, ["SIX"] = 6, ["SEVEN"] = 7, ["EIGHT"] = 8, ["NINE"] = 9, ["NINER"] = 9,
    };

    private static readonly Dictionary<string, int> TeenWords = new()
    {
        ["TEN"] = 10, ["ELEVEN"] = 11, ["TWELVE"] = 12, ["THIRTEEN"] = 13, ["FOURTEEN"] = 14, ["FIFTEEN"] = 15,
        ["SIXTEEN"] = 16, ["SEVENTEEN"] = 17, ["EIGHTEEN"] = 18, ["NINETEEN"] = 19,
    };

    private static readonly Dictionary<string, int> TensWords = new()
    {
        ["TWENTY"] = 20, ["THIRTY"] = 30, ["FORTY"] = 40, ["FIFTY"] = 50, ["SIXTY"] = 60, ["SEVENTY"] = 70, ["EIGHTY"] = 80, ["NINETY"] = 90,
    };

    private static readonly Dictionary<string, string> Alphabet = new()
    {
        ["ALPHA"] = "A", ["ALFA"] = "A", ["BRAVO"] = "B", ["CHARLIE"] = "C", ["DELTA"] = "D", ["ECHO"] = "E", ["FOXTROT"] = "F",
        ["GOLF"] = "G", ["HOTEL"] = "H", ["INDIA"] = "I", ["JULIET"] = "J", ["JULIETT"] = "J", ["KILO"] = "K", ["LIMA"] = "L",
        ["MIKE"] = "M", ["NOVEMBER"] = "N", ["OSCAR"] = "O", ["PAPA"] = "P", ["QUEBEC"] = "Q", ["ROMEO"] = "R", ["SIERRA"] = "S",
        ["TANGO"] = "T", ["UNIFORM"] = "U", ["VICTOR"] = "V", ["WHISKEY"] = "W", ["WHISKY"] = "W", ["XRAY"] = "X", ["YANKEE"] = "Y",
        ["ZULU"] = "Z",
        // Whisper's other spellings of the alphabet
        ["EKO"] = "E", ["ECKO"] = "E", ["JULIETTE"] = "J", ["KEELO"] = "K", ["KEYLO"] = "K", ["QUEBECK"] = "Q", ["YANKY"] = "Y",
    };

    public static string Normalize(string? transcript)
    {
        string t = (transcript ?? "").ToUpperInvariant();
        t = ThousandsComma().Replace(t, "$1$2");                // 5,000 -> 5000
        t = DecimalPoint().Replace(t, "$1 DOT $2");             // 121.635 -> 121 DOT 635
        t = t.Replace("X-RAY", "XRAY");
        t = NotAlphanumeric().Replace(t, " ");
        t = LetterDigitBoundary().Replace(t, " ");              // FL120 -> FL 120
        var tokens = t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Map).ToList();
        return string.Join(" ", Merge(tokens).Select(x => x.Text));
    }

    /// <summary>The normalised text without spaces, for "does the transcript contain this name" checks.</summary>
    public static string Squash(string text) => new(text.Where(char.IsAsciiLetterOrDigit).ToArray());

    private static Tok Map(string w)
    {
        if (DigitWords.TryGetValue(w, out int d)) return new(d.ToString(CultureInfo.InvariantCulture), Kind.Digits, FromWord: true);
        if (TeenWords.TryGetValue(w, out int teen)) return new(teen.ToString(CultureInfo.InvariantCulture), Kind.Digits, FromWord: true);
        if (TensWords.TryGetValue(w, out int tens)) return new(tens.ToString(CultureInfo.InvariantCulture), Kind.Digits, FromWord: true);
        if (w is "DOT" or "DECIMAL" or "POINT") return new(".", Kind.Dot);
        if (Alphabet.TryGetValue(w, out var letter)) return new(letter, Kind.Letter);
        if (w.All(char.IsAsciiDigit)) return new(w, Kind.Digits);
        return new(w, Kind.Word);
    }

    private static IEnumerable<Tok> Merge(List<Tok> tokens)
    {
        // "twenty five" -> 25
        for (int i = 0; i + 1 < tokens.Count; i++)
        {
            if (tokens[i].FromWord && TensWords.ContainsValue(int.Parse(tokens[i].Text, CultureInfo.InvariantCulture))
                && tokens[i + 1] is { FromWord: true, Kind: Kind.Digits, Text.Length: 1 } unit && unit.Text != "0")
            {
                tokens[i] = new((int.Parse(tokens[i].Text, CultureInfo.InvariantCulture) + int.Parse(unit.Text, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture), Kind.Digits, true);
                tokens.RemoveAt(i + 1);
            }
        }

        for (int i = 0; i < tokens.Count;)
        {
            var tok = tokens[i];
            if (tok.Kind == Kind.Digits)
            {
                string number = DigitRun(tokens, ref i, allowDot: true);
                // "5 thousand" -> 5000; a run of digits too long to be an altitude is left as it is (no overflow)
                if (!number.Contains('.') && number.Length <= 6 && i < tokens.Count && tokens[i].Text is "THOUSAND" or "HUNDRED")
                {
                    long value = 0;
                    if (tokens[i].Text == "THOUSAND")
                    {
                        value = long.Parse(number, CultureInfo.InvariantCulture) * 1000;
                        i++;
                        int save = i;
                        if (i < tokens.Count && tokens[i].Kind == Kind.Digits)
                        {
                            string hundreds = DigitRun(tokens, ref i, allowDot: false);
                            if (hundreds.Length <= 6 && i < tokens.Count && tokens[i].Text == "HUNDRED") { value += long.Parse(hundreds, CultureInfo.InvariantCulture) * 100; i++; }
                            else i = save;
                        }
                    }
                    else
                    {
                        value = long.Parse(number, CultureInfo.InvariantCulture) * 100;
                        i++;
                    }
                    yield return new(value.ToString(CultureInfo.InvariantCulture), Kind.Digits);
                }
                else yield return new(number, Kind.Digits);
            }
            else if (tok.Kind == Kind.Letter)
            {
                string letters = "";
                while (i < tokens.Count && tokens[i].Kind == Kind.Letter) letters += tokens[i++].Text;
                yield return new(letters, Kind.Word);
            }
            else if (tok is { Kind: Kind.Word, Text.Length: 1 } && i + 1 < tokens.Count && tokens[i + 1] is { Kind: Kind.Word, Text.Length: 1 })
            {
                // "Q N H", "I F R": letters said one by one
                string letters = "";
                while (i < tokens.Count && tokens[i] is { Kind: Kind.Word, Text.Length: 1 }) letters += tokens[i++].Text;
                yield return new(letters, Kind.Word);
            }
            else if (tok.Kind == Kind.Dot) i++; // a "point" that is not inside a number
            else
            {
                yield return tok;
                i++;
            }
        }
    }

    /// <summary>Consecutive digit groups (and one decimal point between digits) as one number.</summary>
    private static string DigitRun(List<Tok> tokens, ref int i, bool allowDot)
    {
        string s = "";
        bool dot = false;
        while (i < tokens.Count)
        {
            var t = tokens[i];
            if (t.Kind == Kind.Digits) { s += t.Text; i++; }
            else if (allowDot && !dot && t.Kind == Kind.Dot && s.Length > 0 && i + 1 < tokens.Count && tokens[i + 1].Kind == Kind.Digits)
            {
                s += ".";
                dot = true;
                i++;
            }
            else break;
        }
        return s;
    }

    [GeneratedRegex(@"(\d),(\d{3})(?!\d)")]
    private static partial Regex ThousandsComma();

    [GeneratedRegex(@"(\d)\.(\d)")]
    private static partial Regex DecimalPoint();

    [GeneratedRegex(@"[^A-Z0-9]+")]
    private static partial Regex NotAlphanumeric();

    [GeneratedRegex(@"(?<=[A-Z])(?=\d)|(?<=\d)(?=[A-Z])")]
    private static partial Regex LetterDigitBoundary();
}
