using System.Text;

namespace OpenSkyAtc.Host;

/// <summary>
/// The flight log file (Core/Recording/FlightLog): %LOCALAPPDATA%\OpenSkyATC\recordings\&lt;time&gt; &lt;route&gt; &lt;callsign&gt;.flightlog.jsonl,
/// written during the flight and flushed often, so a crash still leaves a usable log. Only the newest
/// <see cref="Keep"/> logs are kept. They stay on this PC; the owner sends one to report a problem.
/// </summary>
public sealed class FlightLogWriter : IDisposable
{
    public const int Keep = 20;
    public const string Extension = ".flightlog.jsonl";

    private readonly object _lock = new();
    private readonly StreamWriter _writer;
    private int _sinceFlush;

    public static string Folder { get; } = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenSkyATC", "recordings");

    public string Path { get; }

    private FlightLogWriter(string path)
    {
        Path = path;
        _writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
    }

    /// <summary>Opens a new log for a flight and removes the oldest ones beyond <see cref="Keep"/>.</summary>
    public static FlightLogWriter Open(string origin, string destination, string callsign, string? folder = null)
    {
        string dir = folder ?? Folder;
        Directory.CreateDirectory(dir);
        var old = Directory.GetFiles(dir, "*" + Extension).OrderByDescending(File.GetLastWriteTimeUtc).Skip(Keep - 1);
        foreach (var f in old)
        {
            try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        string name = $"{DateTime.Now:yyyyMMdd-HHmmss} {Safe(origin)}-{Safe(destination)} {Safe(callsign)}{Extension}";
        return new FlightLogWriter(System.IO.Path.Combine(dir, name));
    }

    /// <summary>Appends a line; state lines are flushed every few seconds, the others at once.</summary>
    public void WriteLine(string line)
    {
        lock (_lock)
        {
            _writer.WriteLine(line);
            bool state = line.StartsWith("{\"k\":\"state\"", StringComparison.Ordinal);
            if (!state || ++_sinceFlush >= 12)
            {
                _writer.Flush();
                _sinceFlush = 0;
            }
        }
    }

    public void Dispose()
    {
        lock (_lock) _writer.Dispose();
    }

    private static string Safe(string text)
    {
        var bad = System.IO.Path.GetInvalidFileNameChars();
        string s = new(text.Where(c => !bad.Contains(c)).ToArray());
        return s.Length == 0 ? "flight" : s;
    }
}
