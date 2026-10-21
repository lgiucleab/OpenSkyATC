using SherpaOnnx;

namespace OpenSkyAtc.Voice;

/// <summary>Mono audio: samples in -1..1 at <see cref="SampleRate"/>.</summary>
public sealed record AudioClip(float[] Samples, int SampleRate)
{
    public TimeSpan Duration => TimeSpan.FromSeconds((double)Samples.Length / SampleRate);
}

/// <summary>
/// Speaks text with an installed voice through sherpa-onnx, in-process: Kokoro (natural, the default since refactor
/// phase 4) or Piper (VITS, fast). Each model is loaded once and kept. Thread-safe.
/// </summary>
public sealed class VoiceSynthesizer(VoiceStore store) : IDisposable
{
    private readonly Dictionary<string, OfflineTts> _loaded = new();
    private readonly object _lock = new();

    /// <summary>Speech rate: 1 normal; controllers speak a little faster than audiobook readers.</summary>
    public float Speed { get; init; } = 1.1f;

    /// <summary>CPU threads per model: Kokoro gets more, it does more work per sentence; the sim keeps the rest.</summary>
    public static int Threads(VoiceEngine engine) => engine == VoiceEngine.Kokoro ? Math.Clamp(Environment.ProcessorCount / 4, 2, 4) : 2;

    public AudioClip Speak(string text, VoiceRef voice)
    {
        lock (_lock)
        {
            var tts = Load(VoiceCatalog.Get(voice.ModelId));
            int speaker = Math.Clamp(voice.Speaker, 0, Math.Max(0, tts.NumSpeakers - 1));
            var audio = tts.Generate(text, Speed, speaker);
            return new AudioClip(audio.Samples, audio.SampleRate);
        }
    }

    private OfflineTts Load(VoiceModel model)
    {
        if (_loaded.TryGetValue(model.Id, out var tts)) return tts;
        string file = store.ModelFile(model) ?? throw new InvalidOperationException($"The {model.Id} voice is not installed.");
        string dir = store.FolderOf(model);
        var config = new OfflineTtsConfig();
        if (model.Engine == VoiceEngine.Kokoro)
        {
            config.Model.Kokoro.Model = file;
            config.Model.Kokoro.Voices = Path.Combine(dir, "voices.bin");
            config.Model.Kokoro.Tokens = Path.Combine(dir, "tokens.txt");
            config.Model.Kokoro.DataDir = store.DataDirOf(model);
            // English only (the radio is always English): the US lexicon, then espeak-ng for words it lacks. The
            // package's Chinese dictionary and lexicon are not used (and not shipped).
            config.Model.Kokoro.Lexicon = Path.Combine(dir, "lexicon-us-en.txt");
            config.Model.Kokoro.Lang = "en-us";
        }
        else
        {
            config.Model.Vits.Model = file;
            config.Model.Vits.Tokens = Path.Combine(dir, "tokens.txt");
            config.Model.Vits.DataDir = store.DataDirOf(model);
        }
        config.Model.NumThreads = Threads(model.Engine);
        config.Model.Provider = "cpu";
        config.MaxNumSentences = model.Engine == VoiceEngine.Kokoro ? 1 : 4; // Kokoro only takes 1
        tts = new OfflineTts(config);
        _loaded[model.Id] = tts;
        return tts;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var tts in _loaded.Values) tts.Dispose();
            _loaded.Clear();
        }
    }
}
