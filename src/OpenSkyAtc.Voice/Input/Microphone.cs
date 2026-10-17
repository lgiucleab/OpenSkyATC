using NAudio.Wave;

namespace OpenSkyAtc.Voice.Input;

/// <summary>
/// Records the pilot while push-to-talk is held: 16 kHz mono from a WinMM input device. Stop keeps a short tail,
/// because people let go of the button while still saying the last word.
/// </summary>
public sealed class Microphone : IDisposable
{
    public static readonly TimeSpan Tail = TimeSpan.FromMilliseconds(300);

    private readonly int _device;
    private readonly object _lock = new();
    private WaveInEvent? _wave;
    private List<float> _samples = [];

    /// <param name="device">WinMM input device number; -1 is the Windows default.</param>
    public Microphone(int device = -1) => _device = device;

    public bool IsRecording { get; private set; }

    /// <summary>Input devices by number, for the settings window.</summary>
    public static IReadOnlyList<(int Number, string Name)> Devices() =>
        Enumerable.Range(0, WaveInEvent.DeviceCount).Select(i => (i, WaveInEvent.GetCapabilities(i).ProductName)).ToList();

    public void Start()
    {
        lock (_lock)
        {
            if (IsRecording) return;
            _samples = new List<float>(SpeechRecognizer.SampleRate * 10);
            _wave = new WaveInEvent
            {
                DeviceNumber = _device,
                WaveFormat = new WaveFormat(SpeechRecognizer.SampleRate, 16, 1),
                BufferMilliseconds = 50,
            };
            _wave.DataAvailable += OnData;
            _wave.StartRecording();
            IsRecording = true;
        }
    }

    /// <summary>Stops after the tail and returns what was recorded.</summary>
    public async Task<float[]> StopAsync()
    {
        if (!IsRecording) return [];
        await Task.Delay(Tail);
        lock (_lock)
        {
            if (_wave is { } wave)
            {
                wave.DataAvailable -= OnData;
                wave.StopRecording();
                wave.Dispose();
            }
            _wave = null;
            IsRecording = false;
            return _samples.ToArray();
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        lock (_lock)
        {
            for (int i = 0; i + 1 < e.BytesRecorded; i += 2)
                _samples.Add(BitConverter.ToInt16(e.Buffer, i) / 32768f);
        }
    }

    /// <summary>Loudest sample, to tell an empty press from speech.</summary>
    public static float Peak(float[] samples) => samples.Length == 0 ? 0 : samples.Max(Math.Abs);

    public void Dispose()
    {
        lock (_lock)
        {
            _wave?.Dispose();
            _wave = null;
            IsRecording = false;
        }
    }
}
