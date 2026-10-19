namespace OpenSkyAtc.Host;

/// <summary>
/// Unexpected errors the host survives (a sim update, a panel command, a voice call): appended to
/// %LOCALAPPDATA%\OpenSkyATC\logs\errors-&lt;date&gt;.txt so they can be reported, and reported to the user once per kind.
/// </summary>
public static class HostErrors
{
    private static readonly object Lock = new();
    private static readonly HashSet<string> Seen = [];

    public static string Folder { get; internal set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenSkyATC", "logs");

    /// <summary>Writes the error; true the first time this kind of error is seen in this run.</summary>
    public static bool Record(string where, Exception e)
    {
        lock (Lock)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(Path.Combine(Folder, $"errors-{DateTime.Now:yyyyMMdd}.txt"), $"{DateTime.Now:O} [{where}] {e}{Environment.NewLine}{Environment.NewLine}");
            }
            catch (IOException)
            {
                // the log is best effort
            }
            catch (UnauthorizedAccessException)
            {
            }
            return Seen.Add($"{where}|{e.GetType().FullName}|{e.TargetSite?.Name}");
        }
    }
}
