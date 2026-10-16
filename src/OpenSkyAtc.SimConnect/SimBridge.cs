using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.FlightSimulator.SimConnect;
using OpenSkyAtc.Core.Sim;
using SC = Microsoft.FlightSimulator.SimConnect.SimConnect;

namespace OpenSkyAtc.SimConnect;

public enum PushToTalkSource
{
    /// <summary>The key or joystick button set in OpenSkyATC, caught through SimConnect (hold to talk).</summary>
    SimBinding,

    /// <summary>The sim's own "ATC" control (Scroll Lock by default), press to start and press again to stop.</summary>
    AtcKey,
}

public sealed record SimBridgeOptions
{
    /// <summary>Design doc: the brain ticks at 4 Hz.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>How often to try again while the sim is not running or after it closed the connection.</summary>
    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromSeconds(5);

    public string ClientName { get; init; } = "OpenSkyATC";

    /// <summary>
    /// Hold-to-talk input in SimConnect's format ("VK_LCONTROL+T", "joystick:0:button:3"); null for none.
    /// Only seen while the sim has focus. See SimConnect_MapInputEventToClientEvent_EX1 in the SDK docs.
    /// </summary>
    public string? PushToTalkInput { get; init; }

    /// <summary>Take over the sim's "ATC" control as a push-to-talk toggle and keep it from opening the default ATC window.</summary>
    public bool UseAtcKey { get; init; } = true;
}

/// <summary>
/// The live link to MSFS 2024: the user aircraft's <see cref="SimState"/> at 4 Hz, push-to-talk from the sim, and
/// reconnection. <see cref="Run"/> keeps trying to connect every <see cref="SimBridgeOptions.RetryInterval"/>
/// until cancelled. <see cref="SimState.Time"/> only advances while connected, so a gap never runs a brain timer out.
/// Events are raised on the thread that calls <see cref="Run"/>. Call <see cref="SimConnectLibrary.EnsureLoaded"/> first.
/// </summary>
public sealed class SimBridge
{
    private enum Definitions { State, Title }
    private enum Requests { State, Title }
    private enum Events { Pause, PttDown, PttUp, Atc }
    private enum Groups { Atc }
    private enum Inputs { Ptt }

    private readonly SimBridgeOptions _o;
    private readonly Stopwatch _connected = new();
    private TimeSpan _timeBase;
    private PauseFlags _pause;
    private bool _holdDown, _toggleOn;

    public SimBridge(SimBridgeOptions? options = null) => _o = options ?? new SimBridgeOptions();

    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<SimState>? StateReceived;
    public event Action<string>? TitleReceived;

    /// <summary>Push-to-talk went down (true) or up (false); the source says which control did it.</summary>
    public event Action<bool, PushToTalkSource>? PushToTalkChanged;

    /// <summary>Raised when a SimConnect call is refused (for example an invalid push-to-talk input string).</summary>
    public event Action<string>? Warning;

    public bool IsConnected { get; private set; }
    public bool PushToTalkHeld => _holdDown || _toggleOn;

    /// <summary>Connects, polls and reconnects until <paramref name="cancel"/> fires.</summary>
    public void Run(CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            using var signal = new AutoResetEvent(false);
            SC? sim = null;
            try
            {
                sim = new SC(_o.ClientName, IntPtr.Zero, 0, signal, 0);
            }
            catch (COMException)
            {
                // Sim not running yet.
            }

            if (sim is not null)
            {
                try
                {
                    RunConnected(sim, signal, cancel);
                }
                finally
                {
                    sim.Dispose();
                    _timeBase += _connected.Elapsed;
                    _connected.Reset();
                    ReleasePushToTalk();
                    if (IsConnected)
                    {
                        IsConnected = false;
                        Disconnected?.Invoke();
                    }
                }
            }
            if (cancel.WaitHandle.WaitOne(_o.RetryInterval)) return;
        }
    }

    private void RunConnected(SC sim, AutoResetEvent signal, CancellationToken cancel)
    {
        bool quit = false;
        sim.OnRecvQuit += (_, _) => quit = true;
        sim.OnRecvSimobjectData += OnData;
        sim.OnRecvEvent += OnEvent;
        sim.OnRecvException += (_, e) => Warning?.Invoke($"SimConnect refused a call: {(SIMCONNECT_EXCEPTION)e.dwException} (send {e.dwSendID}, parameter {e.dwIndex})");

        foreach (var (name, unit) in RawSimData.Definition)
            sim.AddToDataDefinition(Definitions.State, name, unit, SIMCONNECT_DATATYPE.FLOAT64, 0, SC.SIMCONNECT_UNUSED);
        sim.RegisterDataDefineStruct<RawSimData>(Definitions.State);
        sim.AddToDataDefinition(Definitions.Title, "TITLE", null, SIMCONNECT_DATATYPE.STRING256, 0, SC.SIMCONNECT_UNUSED);
        sim.RegisterDataDefineStruct<RawTitle>(Definitions.Title);
        sim.SubscribeToSystemEvent(Events.Pause, "Pause_EX1");
        SetUpPushToTalk(sim);

        sim.RequestDataOnSimObject(Requests.Title, Definitions.Title, SC.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
        _pause = PauseFlags.None;
        _connected.Restart();
        IsConnected = true;
        Connected?.Invoke();

        var next = _connected.Elapsed;
        while (!cancel.IsCancellationRequested && !quit)
        {
            if (_connected.Elapsed >= next)
            {
                sim.RequestDataOnSimObject(Requests.State, Definitions.State, SC.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
                next += _o.Interval;
                if (next < _connected.Elapsed) next = _connected.Elapsed + _o.Interval; // fell behind: do not burst
            }
            var wait = next - _connected.Elapsed;
            signal.WaitOne(wait > TimeSpan.Zero ? wait : TimeSpan.Zero);
            try
            {
                sim.ReceiveMessage();
            }
            catch (COMException)
            {
                return; // the sim went away without a quit message
            }
        }
    }

    private void SetUpPushToTalk(SC sim)
    {
        if (_o.PushToTalkInput is { Length: > 0 } input)
        {
            // Private client events (names with a period are custom events, not sim events).
            sim.MapClientEventToSimEvent(Events.PttDown, "OpenSkyATC.PttDown");
            sim.MapClientEventToSimEvent(Events.PttUp, "OpenSkyATC.PttUp");
            sim.MapInputEventToClientEvent_EX1(Inputs.Ptt, input, Events.PttDown, 0, Events.PttUp, 0, false);
            sim.SetInputGroupPriority(Inputs.Ptt, SC.SIMCONNECT_GROUP_PRIORITY_HIGHEST);
            sim.SetInputGroupState(Inputs.Ptt, (uint)SIMCONNECT_STATE.ON);
        }
        if (_o.UseAtcKey)
        {
            sim.MapClientEventToSimEvent(Events.Atc, "ATC");
            sim.AddClientEventToNotificationGroup(Groups.Atc, Events.Atc, true);
            sim.SetNotificationGroupPriority(Groups.Atc, SC.SIMCONNECT_GROUP_PRIORITY_HIGHEST_MASKABLE);
        }
    }

    private void OnData(SC sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
    {
        switch ((Requests)data.dwRequestID)
        {
            case Requests.State:
                StateReceived?.Invoke(((RawSimData)data.dwData[0]).ToSimState(_timeBase + _connected.Elapsed, _pause));
                break;
            case Requests.Title:
                TitleReceived?.Invoke(((RawTitle)data.dwData[0]).Title);
                break;
        }
    }

    private void OnEvent(SC sender, SIMCONNECT_RECV_EVENT e)
    {
        switch ((Events)e.uEventID)
        {
            case Events.Pause:
                _pause = (PauseFlags)e.dwData;
                break;
            case Events.PttDown when !_holdDown:
                bool wasHeld = PushToTalkHeld;
                _holdDown = true;
                if (!wasHeld) PushToTalkChanged?.Invoke(true, PushToTalkSource.SimBinding);
                break;
            case Events.PttUp when _holdDown:
                _holdDown = false;
                if (!PushToTalkHeld) PushToTalkChanged?.Invoke(false, PushToTalkSource.SimBinding);
                break;
            case Events.Atc:
                bool before = PushToTalkHeld;
                _toggleOn = !_toggleOn;
                if (PushToTalkHeld != before) PushToTalkChanged?.Invoke(PushToTalkHeld, PushToTalkSource.AtcKey);
                break;
        }
    }

    /// <summary>A lost connection never leaves the microphone open.</summary>
    private void ReleasePushToTalk()
    {
        bool was = PushToTalkHeld;
        _holdDown = _toggleOn = false;
        if (was) PushToTalkChanged?.Invoke(false, PushToTalkSource.SimBinding);
    }
}
