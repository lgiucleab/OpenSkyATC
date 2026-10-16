using System.Diagnostics;
using Microsoft.FlightSimulator.SimConnect;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.SimConnect;

/// <summary>
/// Polls the user aircraft at a fixed rate and raises <see cref="StateReceived"/> with each snapshot.
/// Call <see cref="SimConnectLibrary.EnsureLoaded"/> before creating this class.
/// </summary>
public sealed class SimStateSource : IDisposable
{
    private enum Definitions { State, Title }
    private enum Requests { State, Title }
    private enum Events { Pause }

    private readonly TimeSpan _interval;
    private readonly Stopwatch _clock = new();
    private readonly AutoResetEvent _messageSignal = new(false);
    private Microsoft.FlightSimulator.SimConnect.SimConnect? _sim;
    private PauseFlags _pause;

    public SimStateSource(TimeSpan interval) => _interval = interval;

    public event Action<SimState>? StateReceived;
    public event Action<string>? TitleReceived;
    public event Action? Disconnected;

    /// <summary>Tries to connect. Returns false when the sim is not running.</summary>
    public bool TryConnect(string clientName)
    {
        try
        {
            _sim = new Microsoft.FlightSimulator.SimConnect.SimConnect(clientName, IntPtr.Zero, 0, _messageSignal, 0);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }

        _sim.OnRecvSimobjectData += OnData;
        _sim.OnRecvEvent += OnEvent;
        _sim.OnRecvQuit += (_, _) => Disconnected?.Invoke();

        foreach (var (name, unit) in RawSimData.Definition)
            _sim.AddToDataDefinition(Definitions.State, name, unit, SIMCONNECT_DATATYPE.FLOAT64, 0, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
        _sim.RegisterDataDefineStruct<RawSimData>(Definitions.State);

        _sim.AddToDataDefinition(Definitions.Title, "TITLE", null, SIMCONNECT_DATATYPE.STRING256, 0, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
        _sim.RegisterDataDefineStruct<RawTitle>(Definitions.Title);

        _sim.SubscribeToSystemEvent(Events.Pause, "Pause_EX1");
        _sim.RequestDataOnSimObject(Requests.Title, Definitions.Title, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
            SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
        _clock.Restart();
        return true;
    }

    /// <summary>Runs the poll and message loop until cancelled or the sim disconnects.</summary>
    public void Run(CancellationToken cancel)
    {
        var sim = _sim ?? throw new InvalidOperationException("Not connected.");
        bool quit = false;
        Disconnected += () => quit = true;
        var next = _clock.Elapsed;

        while (!cancel.IsCancellationRequested && !quit)
        {
            if (_clock.Elapsed >= next)
            {
                sim.RequestDataOnSimObject(Requests.State, Definitions.State, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
                next += _interval;
            }

            var wait = next - _clock.Elapsed;
            _messageSignal.WaitOne(wait > TimeSpan.Zero ? wait : TimeSpan.Zero);
            try
            {
                sim.ReceiveMessage();
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Disconnected?.Invoke();
            }
        }
    }

    private void OnData(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
    {
        switch ((Requests)data.dwRequestID)
        {
            case Requests.State:
                var raw = (RawSimData)data.dwData[0];
                StateReceived?.Invoke(raw.ToSimState(_clock.Elapsed, _pause));
                break;
            case Requests.Title:
                TitleReceived?.Invoke(((RawTitle)data.dwData[0]).Title);
                break;
        }
    }

    private void OnEvent(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_EVENT e)
    {
        if ((Events)e.uEventID == Events.Pause) _pause = (PauseFlags)e.dwData;
    }

    public void Dispose()
    {
        _sim?.Dispose();
        _sim = null;
        _messageSignal.Dispose();
    }
}
