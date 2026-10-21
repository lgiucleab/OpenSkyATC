using OpenSkyAtc.Voice;

namespace OpenSkyAtc.Recorder;

/// <summary>
/// "voices list | install [ID...|all] | say TEXT [--voice MODEL:SPEAKER] [--out FILE] [--clean] | audition MODEL [--from N] [--count N]".
/// Voices download on first use from the sherpa-onnx tts-models release into %LOCALAPPDATA%\OpenSkyATC\voices.
/// </summary>
internal static class VoiceCommands
{
    public const string Usage =
        "voices list\n" +
        "voices install [vctk libritts_r joe norman john cori | all]\n" +
        "voices say \"Iberia 1234, climb flight level 120\" [--voice vctk:12] [--out file.wav] [--clean]\n" +
        "voices audition MODEL [--from N] [--count N]\n" +
        "voices play FILE.wav";

    public static int Run(string[] args)
    {
        var store = new VoiceStore();
        string sub = args.Length > 1 ? args[1] : "list";
        try
        {
            return sub switch
            {
                "list" => List(store),
                "install" => Install(store, args.Skip(2).ToList()),
                "say" when args.Length > 2 => Say(store, args[2], Option(args, "--voice"), Option(args, "--out"), args.Contains("--clean")),
                "play" when args.Length > 2 => Play(args[2]),
                "bench" => Bench(store, (Option(args, "--voices") ?? "kokoro:26,vctk:12").Split(',')),
                "audition" when args.Length > 2 => Audition(store, args[2], int.Parse(Option(args, "--from") ?? "0"), int.Parse(Option(args, "--count") ?? "10")),
                _ => Help(),
            };
        }
        catch (Exception e) when (e is HttpRequestException or InvalidOperationException or ArgumentException or InvalidDataException)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }

    private static int Help()
    {
        Console.Error.WriteLine("Usage:\n" + Usage);
        return 2;
    }

    private static int List(VoiceStore store)
    {
        Console.WriteLine($"Voices folder: {store.Root}");
        foreach (var m in VoiceCatalog.All)
            Console.WriteLine($"  {m.Id,-11} {(store.IsInstalled(m) ? "installed" : "not installed"),-14} {m.Speakers.Count,3} voice(s)  {m.Licence,-14} {m.Credit}");
        return 0;
    }

    private static int Install(VoiceStore store, List<string> ids)
    {
        var models = ids.Count == 0 || ids.Contains("all") ? VoiceCatalog.All : ids.Select(VoiceCatalog.Get).ToList();
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        foreach (var m in models)
        {
            if (store.IsInstalled(m)) { Console.WriteLine($"{m.Id}: already installed"); continue; }
            Console.Write($"{m.Id}: downloading {m.DownloadUrl} ");
            int lastPct = -1;
            var progress = new SyncProgress(p =>
            {
                int pct = p.Total is > 0 ? (int)(p.Received * 100 / p.Total.Value) : -1;
                if (pct >= 0 && pct / 10 != lastPct / 10) { Console.Write($"{pct}% "); lastPct = pct; }
            });
            store.InstallAsync(m, http, progress).GetAwaiter().GetResult();
            Console.WriteLine("done");
        }
        return 0;
    }

    private static int Say(VoiceStore store, string text, string? voiceText, string? output, bool clean)
    {
        var voice = voiceText is null ? new VoiceRef(store.Installed().FirstOrDefault()?.Id ?? "vctk", 0) : VoiceRef.Parse(voiceText);
        using var tts = new VoiceSynthesizer(store);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var clip = tts.Speak(text, voice);
        Console.WriteLine($"{voice}: {clip.Duration.TotalSeconds:0.0} s of audio in {sw.ElapsedMilliseconds} ms (first use includes loading the model)");
        if (!clean) clip = RadioFilter.Apply(clip, seed: 1);
        if (output is not null)
        {
            Wav.Write(output, clip);
            Console.WriteLine($"Written to {Path.GetFullPath(output)}");
            return 0;
        }
        PlayAndWait(clip);
        return 0;
    }

    private static readonly string[] BenchCalls =
    [
        "Iberia one two three four, Barajas Delivery, cleared to Barcelona via PINA four November departure, climb altitude six thousand feet, squawk four seven two one",
        "Iberia one two three four, taxi to holding point Lima one runway three six left via Echo Bravo, Mike, hold short runway three six right",
        "Iberia one two three four, wind zero eight zero degrees, five knots, runway three six left, cleared for takeoff",
        "Iberia one two three four, climb flight level two four zero",
        "Iberia one two three four, contact Madrid Control one three two decimal one five five, good day",
    ];

    /// <summary>
    /// Time per call once the voice is loaded (what a flight sees): load time, then each call's synthesis time and
    /// the real-time factor (audio seconds per second of work; above 1 means faster than it plays).
    /// </summary>
    private static int Bench(VoiceStore store, string[] voices)
    {
        bool sim = System.Diagnostics.Process.GetProcessesByName("FlightSimulator2024").Length > 0;
        Console.WriteLine($"{Environment.ProcessorCount} cores; MSFS {(sim ? "running" : "not running")}");
        foreach (var v in voices.Select(VoiceRef.Parse))
        {
            using var tts = new VoiceSynthesizer(store);
            var load = System.Diagnostics.Stopwatch.StartNew();
            tts.Speak("Roger", v);
            load.Stop();
            var times = new List<(double Audio, double Work)>();
            foreach (var call in BenchCalls)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var clip = tts.Speak(call, v);
                times.Add((clip.Duration.TotalSeconds, sw.Elapsed.TotalSeconds));
            }
            Console.WriteLine($"{v,-12} load {load.Elapsed.TotalSeconds:0.0} s | per call mean {times.Average(t => t.Work):0.00} s, max {times.Max(t => t.Work):0.00} s | x{times.Sum(t => t.Audio) / times.Sum(t => t.Work):0.0} real time | threads {VoiceSynthesizer.Threads(VoiceCatalog.Get(v.ModelId).Engine)}");
        }
        return 0;
    }

    /// <summary>Plays sample lines with several speakers of a model, to choose the clear ones.</summary>
    private static int Audition(VoiceStore store, string model, int from, int count)
    {
        var m = VoiceCatalog.Get(model);
        using var tts = new VoiceSynthesizer(store);
        foreach (int speaker in m.Speakers.Where(s => s >= from).Take(count))
        {
            var voice = new VoiceRef(m.Id, speaker);
            Console.WriteLine($"{voice}");
            PlayAndWait(RadioFilter.Apply(tts.Speak($"Speaker {speaker}. Iberia one two three four, climb flight level one two zero, contact Madrid Control one three three decimal one.", voice), speaker));
            Thread.Sleep(400);
        }
        return 0;
    }

    private static int Play(string path)
    {
        using var reader = new NAudio.Wave.AudioFileReader(path);
        using var output = new NAudio.Wave.WaveOutEvent();
        output.Init(reader);
        output.Play();
        Console.WriteLine($"Playing {Path.GetFileName(path)} ({reader.TotalTime:mm\\:ss}), Ctrl+C to stop...");
        while (output.PlaybackState == NAudio.Wave.PlaybackState.Playing) Thread.Sleep(200);
        return 0;
    }

    private static void PlayAndWait(AudioClip clip)
    {
        using var player = new ClipPlayer();
        using var done = new ManualResetEventSlim();
        player.Finished += done.Set;
        player.Play(clip);
        done.Wait(clip.Duration + TimeSpan.FromSeconds(2));
    }

    /// <summary>Reports on the downloading thread, in order (Progress&lt;T&gt; posts to the thread pool and reorders).</summary>
    private sealed class SyncProgress(Action<(long Received, long? Total)> report) : IProgress<(long Received, long? Total)>
    {
        public void Report((long Received, long? Total) value) => report(value);
    }

    private static string? Option(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
