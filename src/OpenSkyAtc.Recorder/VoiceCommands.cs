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
        using var tts = new PiperSynthesizer(store);
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

    /// <summary>Plays sample lines with several speakers of a model, to choose the clear ones.</summary>
    private static int Audition(VoiceStore store, string model, int from, int count)
    {
        var m = VoiceCatalog.Get(model);
        using var tts = new PiperSynthesizer(store);
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
