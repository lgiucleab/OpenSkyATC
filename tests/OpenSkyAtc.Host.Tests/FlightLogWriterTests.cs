using OpenSkyAtc.Host;

namespace OpenSkyAtc.Host.Tests;

public class FlightLogWriterTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "openskyatc-flightlog-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Lines_AreReadableWhileTheFlightRuns()
    {
        string folder = TempFolder();
        try
        {
            using var log = FlightLogWriter.Open("LEMD", "LEBL", "IBE1234", folder);
            log.WriteLine("{\"k\":\"start\"}");
            log.WriteLine("{\"k\":\"intent\",\"after\":0}");
            Assert.EndsWith("LEMD-LEBL IBE1234.flightlog.jsonl", log.Path);
            using var read = new FileStream(log.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(read);
            Assert.Equal(["{\"k\":\"start\"}", "{\"k\":\"intent\",\"after\":0}"], reader.ReadToEnd().ReplaceLineEndings("\n").TrimEnd().Split('\n'));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void WritingAfterTheEnd_IsIgnored()
    {
        string folder = TempFolder();
        try
        {
            var log = FlightLogWriter.Open("LEMD", "LEBL", "IBE1234", folder);
            log.WriteLine("{\"k\":\"start\"}");
            log.Dispose();
            log.WriteLine("{\"k\":\"state\"}"); // a late tick from the sim thread
            log.Dispose();
            Assert.Single(File.ReadAllLines(log.Path));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void OnlyTheNewestLogsAreKept()
    {
        string folder = TempFolder();
        Directory.CreateDirectory(folder);
        try
        {
            for (int i = 0; i < FlightLogWriter.Keep + 5; i++)
            {
                string f = Path.Combine(folder, $"old-{i:00}{FlightLogWriter.Extension}");
                File.WriteAllText(f, "");
                File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddHours(-100 + i));
            }
            using var log = FlightLogWriter.Open("LEMD", "LEBL", "IBE1234", folder);
            var left = Directory.GetFiles(folder, "*" + FlightLogWriter.Extension);
            Assert.Equal(FlightLogWriter.Keep, left.Length);
            Assert.DoesNotContain(left, f => f.EndsWith($"old-00{FlightLogWriter.Extension}"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
