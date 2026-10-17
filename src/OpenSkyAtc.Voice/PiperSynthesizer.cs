using SherpaOnnx;

namespace OpenSkyAtc.Voice;

/// <summary>Mono audio: samples in -1..1 at <see cref="SampleRate"/>.</summary>
public sealed record AudioClip(float[] Samples, int SampleRate)
{
    public TimeSpan Duration => TimeSpan.FromSeconds((double)Samples.Length / SampleRate);
}

/// <summary>
/// Speaks text with an installed Piper voice through sherpa-onnx, in-process. Each model is loaded once (about a
/// second) and kept; synthesis of an ATC sentence takes a fraction of a second on CPU. Thread-safe.
/// </summary>
public sealed class PiperSynthesizer(VoiceStore store) : IDisposable
{
    private readonly Dictionary<string, OfflineTts> _loaded = new();
    private readonly object _lock = new();

    /// <summary>Speech rate: 1 normal; controllers speak a little faster than audiobook readers.</summary>
    public float Speed { get; init; } = 1.1f;

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
        config.Model.Vits.Model = file;
        config.Model.Vits.Tokens = Path.Combine(dir, "tokens.txt");
        config.Model.Vits.DataDir = Path.Combine(dir, "espeak-ng-data");
        config.Model.NumThreads = 2;
        config.Model.Provider = "cpu";
        config.MaxNumSentences = 4;
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
