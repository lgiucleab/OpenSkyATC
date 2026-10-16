using System.Globalization;

namespace OpenSkyAtc.Replay.Tests.Harness;

/// <summary>A scripted pilot call. <see cref="Args"/> maps names to values; a null value means "leave this out".</summary>
public sealed record ScriptedIntent(TimeSpan Time, string Name, IReadOnlyDictionary<string, string?> Args);

/// <summary>
/// Pilot calls to make at given sim times. One per line: "&lt;time&gt; &lt;Intent&gt; [key=value ...] [-key ...]",
/// e.g. "00:20 Readback squawk=1234 -initialclimb". Time is mm:ss or hh:mm:ss[.fff].
/// Blank lines and lines starting with # are ignored.
/// </summary>
public static class IntentScript
{
    private static readonly string[] TimeFormats = [@"m\:ss", @"mm\:ss", @"h\:mm\:ss", @"hh\:mm\:ss", @"hh\:mm\:ss\.fff", @"mm\:ss\.fff"];

    public static IReadOnlyList<ScriptedIntent> Parse(string text)
    {
        var result = new List<ScriptedIntent>();
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || !TimeSpan.TryParseExact(parts[0], TimeFormats, CultureInfo.InvariantCulture, out var time))
                throw new FormatException($"Script line {i + 1}: expected '<time> <intent> [args]', got '{line}'.");
            if (!IntentFactory.IsKnown(parts[1]))
                throw new FormatException($"Script line {i + 1}: unknown intent '{parts[1]}'.");

            var args = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var arg in parts.Skip(2))
            {
                if (arg.StartsWith('-')) args[arg[1..]] = null;
                else if (arg.Split('=', 2) is [var k, var v]) args[k] = v;
                else throw new FormatException($"Script line {i + 1}: bad argument '{arg}'.");
            }
            result.Add(new ScriptedIntent(time, parts[1], args));
        }
        return result.OrderBy(s => s.Time).ToArray();
    }
}
