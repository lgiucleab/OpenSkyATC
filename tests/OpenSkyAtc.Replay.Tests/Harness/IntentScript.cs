using System.Globalization;
using OpenSkyAtc.Core.Brain;

namespace OpenSkyAtc.Replay.Tests.Harness;

public sealed record ScriptedIntent(TimeSpan Time, PilotIntent Intent);

/// <summary>
/// Pilot intents to apply at given sim times. One per line: "mm:ss Kind" or "hh:mm:ss[.fff] Kind".
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

            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !TimeSpan.TryParseExact(parts[0], TimeFormats, CultureInfo.InvariantCulture, out var time))
                throw new FormatException($"Script line {i + 1}: expected '<time> <intent>', got '{line}'.");
            if (!Enum.TryParse<PilotIntentKind>(parts[1], ignoreCase: false, out var kind))
                throw new FormatException($"Script line {i + 1}: unknown intent '{parts[1]}'.");

            result.Add(new ScriptedIntent(time, new PilotIntent(kind)));
        }
        return result.OrderBy(s => s.Time).ToArray();
    }
}
