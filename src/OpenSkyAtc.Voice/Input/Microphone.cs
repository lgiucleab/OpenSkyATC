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

    // A stop waits for the tail; a new press during it ends the tail at once, so the next call is recorded.
    private TaskCompletionSource? _tailCut;
    private float[]? _stopped;

    /// <param name="device">WinMM input device number; -1 is the Windows default.</param>
    public Microphone(int device = -1) => _device = device;

    public bool IsRecording { get; private set; }

    /// <summary>Input devices by number, for the settings window.</summary>
    public static IReadOnlyList<(int Number, string Name)> Devices() =>
        Enumerable.Range(0, WaveInEvent.DeviceCount).Select(i => (i, WaveInEvent.GetCapabilities(i).ProductName)).ToList();

    /// <summary>
    /// Starts recording; false when the microphone cannot be opened (unplugged, asleep, used exclusively by another
    /// program, or no input device), with the reason in <paramref name="error"/>.
    /// </summary>
    public bool Start(out string? error)
    {
        lock (_lock)
        {
            error = null;
            if (_tailCut is not null) FinishLocked(); // pressed again within the previous call's tail
            if (IsRecording) return true;
            _samples = new List<float>(SpeechRecognizer.SampleRate * 10);
            var wave = new WaveInEvent
            {
                DeviceNumber = _device,
                WaveFormat = new WaveFormat(SpeechRecognizer.SampleRate, 16, 1),
                BufferMilliseconds = 50,
            };
            wave.DataAvailable += OnData;
            try
            {
                wave.StartRecording();
            }
            catch (NAudio.MmException e)
            {
                wave.DataAvailable -= OnData;
                wave.Dispose();
                error = e.Result == NAudio.MmResult.BadDeviceId ? "no microphone found (is the headset connected?)" : e.Message;
                return false;
            }
            _wave = wave;
            IsRecording = true;
            return true;
        }
    }

    /// <summary>Stops after the tail (or at once when a new press comes first) and returns what was recorded.</summary>
    public async Task<float[]> StopAsync()
    {
        Task cut;
        lock (_lock)
        {
            if (!IsRecording || _tailCut is not null) return [];
            _tailCut = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            cut = _tailCut.Task;
        }
        await Task.WhenAny(Task.Delay(Tail), cut);
        lock (_lock)
        {
            if (_tailCut is not null) FinishLocked();
            var result = _stopped ?? [];
            _stopped = null;
            return result;
        }
    }

    /// <summary>Ends the current recording and keeps its samples for the waiting <see cref="StopAsync"/> (lock held).</summary>
    private void FinishLocked()
    {
        if (_wave is { } wave)
        {
            wave.DataAvailable -= OnData;
            wave.StopRecording();
            wave.Dispose();
        }
        _wave = null;
        IsRecording = false;
        _stopped = _samples.ToArray();
        _tailCut?.TrySetResult();
        _tailCut = null;
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
            _tailCut?.TrySetResult();
            _tailCut = null;
        }
    }
}
