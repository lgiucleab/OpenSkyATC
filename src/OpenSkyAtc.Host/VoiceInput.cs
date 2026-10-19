using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Voice.Input;

namespace OpenSkyAtc.Host;

/// <summary>What the pilot said, as heard and as understood (with the audio, prompt and context, for the voice clips).</summary>
public sealed record HeardCall(string Text, double Confidence, PilotIntent Intent, TimeSpan Took)
{
    public float[] Audio { get; init; } = [];
    public string Prompt { get; init; } = "";
    public ParseContext? Context { get; init; }
}

/// <summary>
/// Voice in for one flight (step 14): records while push-to-talk is held, then Whisper and the intent parser turn the
/// call into a <see cref="PilotIntent"/>. A call Whisper is unsure of, or that parses to nothing, becomes
/// <see cref="Unreadable"/> so ATC says "say again" instead of guessing. Presses that are too short or silent are
/// ignored (a click of the button is not a call).
/// </summary>
public sealed class VoiceInput : IDisposable
{
    public static readonly TimeSpan MinimumCall = TimeSpan.FromMilliseconds(400);
    public const float SilencePeak = 0.02f;

    private readonly Microphone _microphone;
    private readonly SpeechRecognizer _recognizer;
    private readonly double _minConfidence;

    private VoiceInput(Microphone microphone, SpeechRecognizer recognizer, double minConfidence)
    {
        _microphone = microphone;
        _recognizer = recognizer;
        _minConfidence = minConfidence;
    }

    public string Prompt { get; set; } = "";

    /// <summary>The spoken callsign, ending the readback added to the prompt of a call that answers ATC.</summary>
    public string? Callsign { get; set; }
    public ParseContext? Context { get; set; }

    /// <summary>Downloads the model on first use (reporting progress in MB) and loads it.</summary>
    public static async Task<VoiceInput> StartAsync(AppSettings settings, Action<int, int>? downloaded, CancellationToken cancel)
    {
        var model = WhisperModels.Find(settings.WhisperModel) ?? WhisperModels.Default;
        var store = new WhisperModelStore();
        if (!store.IsInstalled(model))
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            await store.InstallAsync(model, http, new Progress<(long Received, long? Total)>(p => downloaded?.Invoke((int)(p.Received >> 20), model.SizeMb)), cancel);
        }
        var recognizer = await Task.Run(() => new SpeechRecognizer(store.PathOf(model)), cancel);
        return new VoiceInput(new Microphone(settings.MicrophoneDevice), recognizer, Math.Clamp(settings.MinSpeechConfidence, 0, 1)) { ModelId = model.Id };
    }

    /// <summary>The Whisper model in use ("small.en-q5_1").</summary>
    public string ModelId { get; private init; } = "";

    public bool IsListening => _microphone.IsRecording;

    /// <summary>Starts recording; false (with the reason) when the microphone cannot be opened.</summary>
    public bool Begin(out string? error) => _microphone.Start(out error);

    /// <summary>Stops recording and works out the call; null when there was no call (too short or silent).</summary>
    public async Task<HeardCall?> EndAsync(Func<ParseContext> context, CancellationToken cancel = default)
    {
        var samples = await _microphone.StopAsync();
        if (samples.Length < MinimumCall.TotalSeconds * SpeechRecognizer.SampleRate || Microphone.Peak(samples) < SilencePeak) return null;
        string prompt = SpeechPrompt.ForCall(Prompt, context().PendingReadback, Callsign);
        var heard = await _recognizer.RecognizeAsync(samples, prompt, cancel);
        if (heard.Text.Length == 0) return null;
        var now = context();
        return new HeardCall(heard.Text, heard.Confidence, Understand(heard.Text, heard.Confidence, now, _minConfidence), heard.Elapsed)
        {
            Audio = samples,
            Prompt = prompt,
            Context = now,
        };
    }

    /// <summary>The intent for a transcript, or <see cref="Unreadable"/> when unsure or not understood.</summary>
    public static PilotIntent Understand(string text, double confidence, ParseContext context, double minConfidence)
    {
        if (confidence < minConfidence) return new Unreadable(text);
        return IntentParser.Parse(text, context).Intent ?? new Unreadable(text);
    }

    public void Dispose()
    {
        _microphone.Dispose();
        _recognizer.Dispose();
    }
}
