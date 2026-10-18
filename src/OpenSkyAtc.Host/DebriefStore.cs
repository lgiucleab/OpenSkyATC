using OpenSkyAtc.Core.Debrief;

namespace OpenSkyAtc.Host;

/// <summary>A saved debrief: its file and contents.</summary>
public sealed record SavedDebrief(string Path, FlightDebrief Debrief);

/// <summary>
/// Debriefs on disk (step 16): one JSON file per flight in %LOCALAPPDATA%\OpenSkyATC\debriefs, named by end time and
/// route ("20261018-1412 LEMD-LEBL IBE1234.json").
/// </summary>
public sealed class DebriefStore(string? folder = null)
{
    public string Folder { get; } = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenSkyATC", "debriefs");

    public string Save(FlightDebrief debrief)
    {
        Directory.CreateDirectory(Folder);
        var ended = debrief.EndedAt ?? DateTimeOffset.Now;
        string name = $"{ended.LocalDateTime:yyyyMMdd-HHmmss} {Safe(debrief.Origin)}-{Safe(debrief.Destination)} {Safe(debrief.Callsign)}.json";
        string path = Path.Combine(Folder, name);
        File.WriteAllText(path, DebriefFormat.ToJson(debrief));
        return path;
    }

    /// <summary>Every readable debrief, newest first (unreadable files are skipped).</summary>
    public IReadOnlyList<SavedDebrief> List()
    {
        if (!Directory.Exists(Folder)) return [];
        var list = new List<SavedDebrief>();
        foreach (var file in Directory.GetFiles(Folder, "*.json"))
        {
            try
            {
                if (DebriefFormat.FromJson(File.ReadAllText(file)) is { } d) list.Add(new SavedDebrief(file, d));
            }
            catch (IOException)
            {
                // being written or locked: skip it this time
            }
        }
        return list.OrderByDescending(s => s.Debrief.EndedAt ?? DateTimeOffset.MinValue).ThenByDescending(s => s.Path).ToList();
    }

    private static string Safe(string text)
    {
        var bad = Path.GetInvalidFileNameChars();
        string s = new(text.Where(c => !bad.Contains(c)).ToArray());
        return s.Length == 0 ? "flight" : s;
    }
}
