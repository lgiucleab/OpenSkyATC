using System.Diagnostics;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Whisper.net;

namespace OpenSkyAtc.Voice.Input;

/// <param name="Text">The transcript, trimmed.</param>
/// <param name="Confidence">Mean token probability (0-1), weighted by segment length; 0 when nothing was heard.</param>
/// <param name="Elapsed">Time Whisper took.</param>
public sealed record Recognition(string Text, double Confidence, TimeSpan Elapsed);

/// <summary>
/// Whisper (whisper.cpp through Whisper.net) for one pilot call at a time: English, no context carried between
/// calls, primed with the flight's words (callsign, stations, SID, runways, fixes) through the initial prompt.
/// </summary>
public sealed class SpeechRecognizer : IDisposable
{
    public const int SampleRate = 16000;

    private readonly WhisperFactory _factory;
    private readonly int _threads;
    private readonly SemaphoreSlim _one = new(1, 1);

    /// <param name="threads">CPU threads for Whisper; 0 means half the cores (at most 4), leaving the rest to the sim.</param>
    public SpeechRecognizer(string modelPath, int threads = 0)
    {
        _factory = WhisperFactory.FromPath(modelPath);
        _threads = threads > 0 ? threads : Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
    }

    /// <param name="samples">Mono audio at 16 kHz, -1..1.</param>
    /// <param name="prompt">Words to prime Whisper with, or null.</param>
    public async Task<Recognition> RecognizeAsync(float[] samples, string? prompt = null, CancellationToken cancel = default)
    {
        await _one.WaitAsync(cancel);
        try
        {
            var watch = Stopwatch.StartNew();
            var builder = _factory.CreateBuilder()
                .WithLanguage("en")
                .WithThreads(_threads)
                .WithNoContext()
                .WithSingleSegment()
                .WithProbabilities();
            if (!string.IsNullOrWhiteSpace(prompt)) builder = builder.WithPrompt(prompt);
            await using var processor = builder.Build();

            var text = new List<string>();
            double weighted = 0, weight = 0;
            await foreach (var segment in processor.ProcessAsync(samples, cancel))
            {
                string t = segment.Text.Trim();
                if (t.Length == 0 || IsNonSpeech(t)) continue;
                text.Add(t);
                weighted += segment.Probability * t.Length;
                weight += t.Length;
            }
            return new Recognition(string.Join(" ", text), weight > 0 ? weighted / weight : 0, watch.Elapsed);
        }
        finally
        {
            _one.Release();
        }
    }

    /// <summary>Whisper's markers for silence or noise: "[BLANK_AUDIO]", "(wind blowing)".</summary>
    private static bool IsNonSpeech(string t) => t is ['[', .., ']'] or ['(', .., ')'] or ['*', .., '*'];

    /// <summary>Any WAV file as mono 16 kHz samples (Piper fixtures, the owner's recordings).</summary>
    public static float[] LoadWav(string path)
    {
        using var reader = new AudioFileReader(path);
        ISampleProvider source = reader;
        if (source.WaveFormat.Channels == 2) source = new StereoToMonoSampleProvider(source);
        else if (source.WaveFormat.Channels > 2) throw new InvalidDataException($"{path}: {source.WaveFormat.Channels} channels.");
        if (source.WaveFormat.SampleRate != SampleRate) source = new WdlResamplingSampleProvider(source, SampleRate);
        var samples = new List<float>();
        var buffer = new float[SampleRate];
        int n;
        while ((n = source.Read(buffer, 0, buffer.Length)) > 0) samples.AddRange(buffer.AsSpan(0, n));
        return samples.ToArray();
    }

    public void Dispose()
    {
        _factory.Dispose();
        _one.Dispose();
    }
}
