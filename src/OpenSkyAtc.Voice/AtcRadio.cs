using System.Collections.Concurrent;
using System.Diagnostics;
using NAudio.Wave;
using OpenSkyAtc.Core.Atis;
using OpenSkyAtc.Core.Messages;
using OpenSkyAtc.Phraseology;

namespace OpenSkyAtc.Voice;

/// <summary>Plays one clip at a time on the default output device; Stop cuts it.</summary>
public sealed class ClipPlayer : IDisposable
{
    private WaveOutEvent? _out;
    private readonly object _lock = new();

    /// <summary>0..1.</summary>
    public float Volume { get; set; } = 0.8f;

    public event Action? Finished;

    public void Play(AudioClip clip)
    {
        lock (_lock)
        {
            StopLocked();
            var format = WaveFormat.CreateIeeeFloatWaveFormat(clip.SampleRate, 1);
            var bytes = new byte[clip.Samples.Length * 4];
            Buffer.BlockCopy(clip.Samples, 0, bytes, 0, bytes.Length);
            var stream = new RawSourceWaveStream(new MemoryStream(bytes), format);
            var output = new WaveOutEvent { Volume = Math.Clamp(Volume, 0f, 1f) };
            output.Init(stream);
            output.PlaybackStopped += (_, _) =>
            {
                bool natural;
                lock (_lock) natural = ReferenceEquals(_out, output);
                if (natural) Finished?.Invoke();
            };
            _out = output;
            output.Play();
        }
    }

    public void Stop()
    {
        lock (_lock) StopLocked();
    }

    private void StopLocked()
    {
        var old = _out;
        _out = null;
        old?.Stop();
        old?.Dispose();
    }

    public void Dispose() => Stop();
}

/// <summary>
/// ATC voice out for a flight: each <see cref="AtcMessage"/> is rendered to speech text (in message order, as the
/// renderer requires), spoken by its unit's voice on a background worker, radio-filtered, and handed to the
/// <see cref="RadioChannel"/>, which plays it when the rules allow. Call <see cref="Update"/> often (the bridge tick)
/// with the radios and push-to-talk state. An ATIS the pilot has tuned (<see cref="Atis"/>) loops on its own player,
/// a little quieter, with a 2 s gap between broadcasts, as long as it stays tuned.
/// </summary>
public sealed class AtcRadio : IDisposable
{
    private readonly IcaoRenderer _renderer;
    private readonly VoiceAssigner _voices;
    private readonly VoiceSynthesizer _tts;
    private readonly RadioChannel _channel = new();
    private readonly ClipPlayer _player = new();
    private readonly BlockingCollection<(AtcMessage? Message, string Speech, string Voice, string? AtisKey)> _toSpeak = new();
    private readonly ClipPlayer _atisPlayer = new();
    private string? _atisKey;
    private AudioClip? _atisClip;
    private TimeSpan _atisNextAt;
    private bool _atisPlaying;

    /// <summary>ATIS volume relative to ATC calls.</summary>
    public const float AtisVolume = 0.7f;
    public static readonly TimeSpan AtisGap = TimeSpan.FromSeconds(2);
    private readonly ConcurrentQueue<RadioTransmission> _ready = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Thread _worker;
    private readonly object _lock = new();
    private readonly int _seed;
    private int _filterSeed;

    public AtcRadio(IcaoRenderer renderer, VoiceAssigner voices, VoiceSynthesizer tts, int seed)
    {
        _renderer = renderer;
        _voices = voices;
        _tts = tts;
        _seed = seed;
        _player.Finished += () => { lock (_lock) _channel.Stopped(_clock.Elapsed); };
        _atisPlayer.Finished += () =>
        {
            lock (_lock)
            {
                _atisPlaying = false;
                _atisNextAt = _clock.Elapsed + AtisGap;
            }
        };
        _worker = new Thread(Work) { IsBackground = true, Name = "ATC voice" };
        _worker.Start();
    }

    public float Volume
    {
        get => _player.Volume;
        set
        {
            _player.Volume = value;
            _atisPlayer.Volume = value * AtisVolume;
        }
    }

    /// <summary>A call is being synthesised, waiting for the channel or playing.</summary>
    public bool IsBusy
    {
        get
        {
            lock (_lock) return _toSpeak.Count > 0 || !_ready.IsEmpty || _channel.Playing is not null || _channel.Waiting > 0 || _synthesising;
        }
    }

    private volatile bool _synthesising;

    /// <summary>Radio effect on (band-pass, hiss, squelch) or clean voices.</summary>
    public bool Filter { get; init; } = true;

    /// <summary>Raised on the caller of <see cref="Update"/> for each start, cut and drop (for logs and the UI).</summary>
    public event Action<RadioAction>? Action;

    /// <summary>A message finished playing (or was cut off): pass it to <c>IAtcBrain.TransmissionHeard</c>.</summary>
    public event Action<AtcMessage>? Heard;

    /// <summary>Raised when a voice fails (model missing or broken); the message is still shown as text.</summary>
    public event Action<string>? Error;

    /// <summary>Queues a message; returns its display text (rendered now, so text and speech match).</summary>
    public string Say(AtcMessage message)
    {
        var rendered = _renderer.Render(message);
        _toSpeak.Add((message, rendered.Speech, message.Unit.Callsign, null));
        return rendered.Display;
    }

    /// <summary>
    /// The ATIS on a tuned frequency, or null for none: a new broadcast is spoken and then looped while it stays
    /// tuned; null (or another airport or letter) stops the old one at once.
    /// </summary>
    public void Atis(AtisReport? report)
    {
        string? key = report is null ? null : $"{report.AirportIcao}:{report.Letter}";
        lock (_lock)
        {
            if (key == _atisKey) return;
            _atisKey = key;
            _atisClip = null;
            _atisPlaying = false;
        }
        _atisPlayer.Stop();
        if (report is not null) _toSpeak.Add((null, _renderer.RenderAtis(report).Speech, report.Unit.Callsign, key));
    }

    /// <param name="isTuned">Whether COM1 or COM2 is on a frequency.</param>
    public void Update(bool pushToTalkHeld, Func<int, bool> isTuned)
    {
        IReadOnlyList<RadioAction> actions;
        lock (_lock)
        {
            var now = _clock.Elapsed;
            while (_ready.TryDequeue(out var t)) _channel.Enqueue(t);
            _channel.PushToTalk(pushToTalkHeld, now);
            actions = _channel.Update(now, isTuned);
        }
        foreach (var a in actions)
        {
            switch (a)
            {
                case StartPlaying s: _player.Play(s.Transmission.Clip); break;
                case CutOff: _player.Stop(); break;
            }
            if (a is FinishedPlaying { Transmission.Message: { } heard }) Heard?.Invoke(heard);
            Action?.Invoke(a);
        }

        AudioClip? atis = null;
        lock (_lock)
        {
            if (_atisClip is not null && !_atisPlaying && _clock.Elapsed >= _atisNextAt)
            {
                atis = _atisClip;
                _atisPlaying = true;
            }
        }
        if (atis is not null) _atisPlayer.Play(atis);
    }

    /// <summary>Stops the current transmission (app closing, sim disconnected).</summary>
    public void Silence()
    {
        _player.Stop();
        Atis(null);
        lock (_lock) _channel.Stopped(_clock.Elapsed);
    }

    private void Work()
    {
        foreach (var (message, speech, unit, atisKey) in _toSpeak.GetConsumingEnumerable())
        {
            _synthesising = message is not null;
            try
            {
                var clean = _tts.Speak(speech, _voices.VoiceFor(unit));
                var clip = Filter ? RadioFilter.Apply(clean, unchecked(_seed + _filterSeed++)) : clean;
                if (message is not null)
                {
                    _ready.Enqueue(new RadioTransmission(message.Unit.Callsign, message.Unit.FrequencyKhz, message.Priority, clip, speech, message));
                }
                else
                {
                    lock (_lock)
                    {
                        if (_atisKey != atisKey) continue; // retuned while it was being spoken
                        _atisClip = clip;
                        _atisNextAt = _clock.Elapsed;
                    }
                }
            }
            catch (Exception e)
            {
                Error?.Invoke($"Could not speak '{speech}': {e.Message}");
            }
            finally
            {
                _synthesising = false;
            }
        }
    }

    public void Dispose()
    {
        _toSpeak.CompleteAdding();
        _worker.Join(TimeSpan.FromSeconds(2));
        _player.Dispose();
        _atisPlayer.Dispose();
    }
}

/// <summary>Writes mono float audio as a 16-bit PCM WAV file.</summary>
public static class Wav
{
    public static void Write(string path, AudioClip clip)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(clip.SampleRate, 16, 1));
        writer.WriteSamples(clip.Samples, 0, clip.Samples.Length);
    }
}
