using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Recording;
using OpenSkyAtc.Host;
using OpenSkyAtc.Voice.Input;

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
                Directory.CreateDirectory(FlightLogWriter.VoiceFolderOf(f));
            }
            using var log = FlightLogWriter.Open("LEMD", "LEBL", "IBE1234", folder);
            var left = Directory.GetFiles(folder, "*" + FlightLogWriter.Extension);
            Assert.Equal(FlightLogWriter.Keep, left.Length);
            Assert.DoesNotContain(left, f => f.EndsWith($"old-00{FlightLogWriter.Extension}"));
            Assert.False(Directory.Exists(Path.Combine(folder, "old-00.voice"))); // clips go with their log
            Assert.True(Directory.Exists(Path.Combine(folder, $"old-{FlightLogWriter.Keep + 4:00}.voice")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void VoiceClips_AreWavsWithAnIndexThatRerunsCanRead()
    {
        string folder = TempFolder();
        try
        {
            var pending = new Readback(new Dictionary<ReadbackItem, string> { [ReadbackItem.Frequency] = "124230" }, 3);
            var audio = Enumerable.Range(0, 8000).Select(i => (float)Math.Sin(i / 10.0) * 0.5f).ToArray();
            var call = new HeardCall("124.230, D-KR", 0.8, new Readback(new Dictionary<ReadbackItem, string> { [ReadbackItem.Frequency] = "124230" }, 3), TimeSpan.FromSeconds(2.5))
            {
                Audio = audio,
                Prompt = "Madrid Departure, D-KR. 124.230, D-KR.",
                Context = new ParseContext([pending]),
            };
            var clips = new VoiceClipWriter(Path.Combine(folder, "flight.voice"));
            clips.Save(1200, call, "small.en-q5_1");
            clips.Save(1300, call with { Text = "say again" }, "small.en-q5_1");

            var index = VoiceClipWriter.ReadIndex(clips.Folder).ToList();
            Assert.Equal(["call-001.wav", "call-002.wav"], index.Select(c => c["clip"]!.GetValue<string>()));
            Assert.Equal(1200, index[0]["after"]!.GetValue<int>());
            var available = Assert.IsType<Readback>(FlightLogFormat.IntentFromJson(index[0]["available"]![0]!));
            Assert.Equal(pending.Items, available.Items);
            var back = SpeechRecognizer.LoadWav(Path.Combine(clips.Folder, "call-001.wav"));
            Assert.Equal(audio.Length, back.Length);
            Assert.InRange(back.Zip(audio).Max(p => Math.Abs(p.First - p.Second)), 0, 0.001);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
