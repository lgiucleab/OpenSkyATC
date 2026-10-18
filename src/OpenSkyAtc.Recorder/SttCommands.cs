using System.Diagnostics;
using NAudio.Wave;
using OpenSkyAtc.Voice;
using OpenSkyAtc.Voice.Input;

namespace OpenSkyAtc.Recorder;

/// <summary>
/// Speech-to-text tools (step 14): "stt list | install MODEL | synth | record [--redo] [--only ID] |
/// bench [--models a,b] [--set piper|owner] | listen [--model ID] [--mic N]".
/// Phrase list: tests/fixtures/speech/phrases.txt; audio in tests/fixtures/speech/piper (committed) and owner (local).
/// </summary>
internal static class SttCommands
{
    public const string Usage =
        "stt list\n" +
        "stt install [tiny.en base.en-q5_1 base.en small.en-q5_1 small.en]\n" +
        "stt synth                 render the phrase list with Piper (tests/fixtures/speech/piper)\n" +
        "stt record [--redo] [--only ID] [--mic N]   record yourself saying the phrase list (tests/fixtures/speech/owner)\n" +
        "stt bench [--models tiny.en,base.en,small.en] [--set piper|owner|both]\n" +
        "stt listen [--model base.en] [--mic N]      say something, see what Whisper heard";

    public static int Run(string[] args)
    {
        var store = new WhisperModelStore();
        string sub = args.Length > 1 ? args[1] : "list";
        try
        {
            return sub switch
            {
                "list" => List(store),
                "install" => Install(store, args.Skip(2).Where(a => !a.StartsWith("--")).ToList()),
                "synth" => Synth(),
                "record" => Record(args.Contains("--redo"), Option(args, "--only"), int.Parse(Option(args, "--mic") ?? "-1")),
                "bench" => Bench(store, (Option(args, "--models") ?? "tiny.en,base.en,small.en").Split(','), Option(args, "--set") ?? "both"),
                "listen" => Listen(store, Option(args, "--model") ?? "base.en", int.Parse(Option(args, "--mic") ?? "-1")),
                _ => Help(),
            };
        }
        catch (Exception e) when (e is HttpRequestException or InvalidOperationException or ArgumentException or InvalidDataException or FileNotFoundException or DirectoryNotFoundException)
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

    private static int List(WhisperModelStore store)
    {
        Console.WriteLine($"Whisper models folder: {store.Root}");
        foreach (var m in WhisperModels.All)
            Console.WriteLine($"  {m.Id,-15} {(store.IsInstalled(m) ? "installed" : "not installed"),-14} {m.SizeMb,4} MB  {m.Label}");
        return 0;
    }

    private static int Install(WhisperModelStore store, List<string> ids)
    {
        var models = ids.Count == 0 ? [WhisperModels.Base] : ids.Select(Model).ToList();
        foreach (var m in models) Ensure(store, m);
        return 0;
    }

    private static WhisperModel Model(string id) => WhisperModels.Find(id) ?? throw new ArgumentException($"Unknown model '{id}'. Known: {string.Join(", ", WhisperModels.All.Select(m => m.Id))}");

    private static void Ensure(WhisperModelStore store, WhisperModel m)
    {
        if (store.IsInstalled(m)) return;
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        Console.Write($"{m.Id}: downloading {m.SizeMb} MB from {m.DownloadUrl.Host} ");
        int last = -1;
        store.InstallAsync(m, http, new SyncProgress(p =>
        {
            int pct = p.Total is > 0 ? (int)(p.Received * 100 / p.Total.Value) : -1;
            if (pct >= 0 && pct / 10 != last / 10) { Console.Write($"{pct}% "); last = pct; }
        })).GetAwaiter().GetResult();
        Console.WriteLine("done");
    }

    private static string SpeechDir()
    {
        for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "tests", "fixtures", "speech");
            if (File.Exists(Path.Combine(candidate, "phrases.txt"))) return candidate;
        }
        throw new DirectoryNotFoundException("Run this inside the OpenSkyATC repository (tests/fixtures/speech/phrases.txt not found).");
    }

    /// <summary>Piper renderings of the phrase list, rotating pilot voices (clean: a cockpit microphone, not a radio).</summary>
    private static int Synth()
    {
        string dir = SpeechDir();
        var phrases = SpeechPhrases.Load(Path.Combine(dir, "phrases.txt"));
        var voiceStore = new VoiceStore();
        var installed = voiceStore.Installed();
        if (installed.Count == 0) throw new InvalidOperationException("No Piper voices installed: run 'voices install' first.");
        var voices = installed.SelectMany(m => m.Speakers.Take(3).Select(s => new VoiceRef(m.Id, s))).ToList();
        string outDir = Path.Combine(dir, "piper");
        Directory.CreateDirectory(outDir);
        using var tts = new PiperSynthesizer(voiceStore);
        foreach (var (p, i) in phrases.Select((p, i) => (p, i)))
        {
            var voice = voices[i % voices.Count];
            var clip = Downsample(tts.Speak(p.Text, voice));
            Wav.Write(Path.Combine(outDir, p.Id + ".wav"), clip);
            Console.WriteLine($"{p.Id,-24} {voice}");
        }
        Console.WriteLine($"{phrases.Count} files in {outDir}");
        return 0;
    }

    /// <summary>16 kHz is all Whisper uses; keeps the committed fixtures small.</summary>
    private static AudioClip Downsample(AudioClip clip)
    {
        if (clip.SampleRate == SpeechRecognizer.SampleRate) return clip;
        var source = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(new ClipProvider(clip), SpeechRecognizer.SampleRate);
        var samples = new List<float>();
        var buffer = new float[4096];
        int n;
        while ((n = source.Read(buffer, 0, buffer.Length)) > 0) samples.AddRange(buffer.AsSpan(0, n));
        return new AudioClip(samples.ToArray(), SpeechRecognizer.SampleRate);
    }

    private sealed class ClipProvider(AudioClip clip) : ISampleProvider
    {
        private int _at;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(clip.SampleRate, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, clip.Samples.Length - _at);
            Array.Copy(clip.Samples, _at, buffer, offset, n);
            _at += n;
            return n;
        }
    }

    /// <summary>The owner reads the phrase list: Enter starts, Enter stops, one WAV per phrase.</summary>
    private static int Record(bool redo, string? only, int mic)
    {
        string dir = SpeechDir();
        var phrases = SpeechPhrases.Load(Path.Combine(dir, "phrases.txt")).Where(p => only is null || p.Id == only).ToList();
        string outDir = Path.Combine(dir, "owner");
        Directory.CreateDirectory(outDir);
        Console.WriteLine($"Recording to {outDir} (kept on this PC, not committed). Speak as on the radio.");
        Console.WriteLine("For each line: Enter to start, Enter to stop; type s + Enter to skip, q + Enter to quit.\n");
        using var microphone = new Microphone(mic);
        foreach (var p in phrases)
        {
            string file = Path.Combine(outDir, p.Id + ".wav");
            if (File.Exists(file) && !redo) continue;
            Console.WriteLine($"[{p.Id}]  {p.Text}");
            Console.Write("  ready? ");
            string? answer = Console.ReadLine()?.Trim();
            if (answer == "q") break;
            if (answer == "s") continue;
            if (!microphone.Start(out var error)) { Console.WriteLine($"  microphone: {error}"); continue; }
            Console.Write("  recording... Enter to stop ");
            Console.ReadLine();
            var samples = microphone.StopAsync().GetAwaiter().GetResult();
            Wav.Write(file, new AudioClip(samples, SpeechRecognizer.SampleRate));
            Console.WriteLine($"  saved {samples.Length / (double)SpeechRecognizer.SampleRate:0.0} s, peak {Microphone.Peak(samples):0.00}{(Microphone.Peak(samples) < 0.05 ? "  (very quiet: check the microphone)" : "")}\n");
        }
        return 0;
    }

    /// <summary>
    /// Accuracy and speed of each model on the recorded phrases: word error rate, calls understood (parsed to the
    /// expected intent is checked by the unit tests; here: the parser's verdict against the phrase text), time per call.
    /// Run it with MSFS open to see the load it adds while flying.
    /// </summary>
    private static int Bench(WhisperModelStore store, string[] modelIds, string set)
    {
        string dir = SpeechDir();
        var phrases = SpeechPhrases.Load(Path.Combine(dir, "phrases.txt"));
        string[] sets = set == "both" ? ["piper", "owner"] : [set];
        var files = sets.SelectMany(s => phrases.Select(p => (Set: s, Phrase: p, File: Path.Combine(dir, s, p.Id + ".wav")))).Where(x => File.Exists(x.File)).ToList();
        if (files.Count == 0) throw new FileNotFoundException("No recordings: run 'stt synth' and/or 'stt record' first.");
        var audio = files.Select(f => (f.Set, f.Phrase, Samples: SpeechRecognizer.LoadWav(f.File))).ToList();
        double audioSeconds = audio.Sum(a => a.Samples.Length) / (double)SpeechRecognizer.SampleRate;
        string prompt = SpeechPrompt.Build("Iberia 1234", ["Madrid Ground", "Madrid Delivery", "Madrid Departure", "Madrid Control", "Barcelona Tower"],
            ["PINA4N", "TOBEK", "LEMD", "LEBL", "runway 36L", "runway 24R"]);
        bool simRunning = Process.GetProcessesByName("FlightSimulator2024").Length > 0 || Process.GetProcessesByName("FlightSimulator").Length > 0;
        Console.WriteLine($"{audio.Count} calls ({audioSeconds:0} s of audio) from {string.Join(" + ", sets)}; {Environment.ProcessorCount} cores; MSFS {(simRunning ? "running" : "not running")}\n");
        Console.WriteLine($"{"model",-15} {"set",-6} {"WER",6} {"exact",6} {"mean ms",8} {"max ms",7} {"x real",7} {"load s",7}");

        foreach (var id in modelIds)
        {
            var model = Model(id.Trim());
            Ensure(store, model);
            var load = Stopwatch.StartNew();
            using var recognizer = new SpeechRecognizer(store.PathOf(model));
            recognizer.RecognizeAsync(new float[SpeechRecognizer.SampleRate], prompt).GetAwaiter().GetResult(); // warm-up
            load.Stop();
            foreach (var s in sets)
            {
                var rows = audio.Where(a => a.Set == s).ToList();
                if (rows.Count == 0) continue;
                var results = rows.Select(a =>
                {
                    var r = recognizer.RecognizeAsync(a.Samples, prompt).GetAwaiter().GetResult();
                    return (a.Phrase, r, Wer: SpeechPhrases.WordErrorRate(a.Phrase.Text, r.Text), Seconds: a.Samples.Length / (double)SpeechRecognizer.SampleRate);
                }).ToList();
                double wer = results.Average(x => x.Wer);
                int exact = results.Count(x => x.Wer == 0);
                double meanMs = results.Average(x => x.r.Elapsed.TotalMilliseconds);
                double maxMs = results.Max(x => x.r.Elapsed.TotalMilliseconds);
                double speed = results.Sum(x => x.Seconds) / results.Sum(x => x.r.Elapsed.TotalSeconds);
                Console.WriteLine($"{model.Id,-15} {s,-6} {wer,6:P1} {exact,3}/{results.Count,-2} {meanMs,8:0} {maxMs,7:0} {speed,7:0.0} {load.Elapsed.TotalSeconds,7:0.0}");
                foreach (var x in results.Where(x => x.Wer > 0))
                    Console.WriteLine($"      {x.Phrase.Id,-24} p={x.r.Confidence:0.00} \"{x.r.Text}\"");
            }
        }
        return 0;
    }

    /// <summary>Microphone check: Enter to start, Enter to stop, prints what Whisper heard and how the parser reads it.</summary>
    private static int Listen(WhisperModelStore store, string modelId, int mic)
    {
        var model = Model(modelId);
        Ensure(store, model);
        using var recognizer = new SpeechRecognizer(store.PathOf(model));
        using var microphone = new Microphone(mic);
        Console.WriteLine($"Microphone {(mic < 0 ? "default" : mic.ToString())}, model {model.Id}. Enter to talk, Enter to stop, q + Enter to quit.");
        var context = new ParseContext([new Core.Conversation.RequestClearance(), new Core.Conversation.RequestPushStart(), new Core.Conversation.RequestTaxi()]);
        while (true)
        {
            if (Console.ReadLine()?.Trim() == "q") return 0;
            if (!microphone.Start(out var error)) { Console.WriteLine($"  microphone: {error}"); continue; }
            Console.Write("  talking... ");
            Console.ReadLine();
            var samples = microphone.StopAsync().GetAwaiter().GetResult();
            var heard = recognizer.RecognizeAsync(samples).GetAwaiter().GetResult();
            var parsed = IntentParser.Parse(heard.Text, context);
            Console.WriteLine($"  \"{heard.Text}\"  p={heard.Confidence:0.00}  {heard.Elapsed.TotalMilliseconds:0} ms  -> {parsed.Intent?.ToString() ?? "not understood"}");
        }
    }

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
