using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.FlightSimulator.SimConnect;
using OpenSkyAtc.Core.Sim;
using OpenSkyAtc.TaxiArrows;
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

    /// <summary>Title of the taxi arrow SimObject in the opensky-atc-taxi-arrows Community package.</summary>
    public string ArrowTitle { get; init; } = "OpenSkyATC Taxi Arrow";
}

/// <summary>
/// The live link to MSFS 2024: the user aircraft's <see cref="SimState"/> at 4 Hz, push-to-talk from the sim, and
/// reconnection. <see cref="Run"/> keeps trying to connect every <see cref="SimBridgeOptions.RetryInterval"/>
/// until cancelled. <see cref="SimState.Time"/> only advances while connected, so a gap never runs a brain timer out.
/// Events are raised on the thread that calls <see cref="Run"/>. Call <see cref="SimConnectLibrary.EnsureLoaded"/> first.
/// </summary>
public sealed class SimBridge
{
    private enum Definitions { State, Title, ArrowPosition }
    private enum Requests { State, Title }
    private enum Events { Pause, PttDown, PttUp, Atc, TogglePushback, GroundService }
    private enum Groups { Atc }

    /// <summary>With GROUPID_IS_PRIORITY the "group" is a priority: 1 is SIMCONNECT_GROUP_PRIORITY_HIGHEST.</summary>
    private enum EventPriority : uint { Highest = 1 }
    private enum Inputs { Ptt }

    private readonly SimBridgeOptions _o;
    private readonly Stopwatch _connected = new();
    private TimeSpan _timeBase;
    private PauseFlags _pause;
    private bool _holdDown, _toggleOn;
    private readonly System.Collections.Concurrent.ConcurrentQueue<(Events Event, uint Data)> _commands = new();
    private readonly object _arrowLock = new();
    private IReadOnlyList<ArrowPose> _wantedArrows = [];
    private bool _arrowsChanged;
    private SimArrows? _arrows;
    private double _groundFt;

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

    /// <summary>Starts or stops the sim's pushback tug (TOGGLE_PUSHBACK). Safe to call from any thread.</summary>
    public void TogglePushback() => _commands.Enqueue((Events.TogglePushback, 0));

    /// <summary>
    /// Sends a ground-service message (GROUND_SERVICE_ATC_MESSAGE): MSFS 2024 steers the pushback tug this way
    /// (TUG_HEADING is refused). Safe to call from any thread.
    /// </summary>
    public void GroundService(GroundServiceMessage message) => _commands.Enqueue((Events.GroundService, (uint)message));

    /// <summary>Arrows currently placed in the sim.</summary>
    public int ArrowCount => _arrows?.Count ?? 0;

    /// <summary>
    /// The taxi arrows to show (from <see cref="TaxiArrowController"/>); applied on the bridge thread at the next
    /// message loop pass. Safe to call from any thread. An empty list removes them all.
    /// </summary>
    public void ShowArrows(IReadOnlyList<ArrowPose> arrows)
    {
        lock (_arrowLock)
        {
            _wantedArrows = arrows;
            _arrowsChanged = true;
        }
    }
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
                    sim.Dispose(); // the sim removes this client's arrows itself
                    _arrows = null;
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
        sim.OnRecvException += (_, e) =>
        {
            if ((SIMCONNECT_EXCEPTION)e.dwException == SIMCONNECT_EXCEPTION.CREATE_OBJECT_FAILED) _arrows?.CreateFailed();
            else Warning?.Invoke($"SimConnect refused a call: {(SIMCONNECT_EXCEPTION)e.dwException} (send {e.dwSendID}, parameter {e.dwIndex})");
        };

        foreach (var (name, unit) in RawSimData.Definition)
            sim.AddToDataDefinition(Definitions.State, name, unit, SIMCONNECT_DATATYPE.FLOAT64, 0, SC.SIMCONNECT_UNUSED);
        sim.RegisterDataDefineStruct<RawSimData>(Definitions.State);
        sim.AddToDataDefinition(Definitions.Title, "TITLE", null, SIMCONNECT_DATATYPE.STRING256, 0, SC.SIMCONNECT_UNUSED);
        sim.RegisterDataDefineStruct<RawTitle>(Definitions.Title);
        sim.SubscribeToSystemEvent(Events.Pause, "Pause_EX1");
        SetUpPushToTalk(sim);
        sim.MapClientEventToSimEvent(Events.TogglePushback, "TOGGLE_PUSHBACK");
        sim.MapClientEventToSimEvent(Events.GroundService, "GROUND_SERVICE_ATC_MESSAGE");
        while (_commands.TryDequeue(out _)) { } // commands from before this connection are stale
        _arrows = new SimArrows(sim, _o.ArrowTitle, Definitions.ArrowPosition, w => Warning?.Invoke(w));
        lock (_arrowLock) _arrowsChanged = true; // a new connection starts with no arrows: place the wanted ones again

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
            ApplyArrows();
            while (_commands.TryDequeue(out var command))
                sim.TransmitClientEvent(SC.SIMCONNECT_OBJECT_ID_USER, command.Event, command.Data, EventPriority.Highest, SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
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
                var state = ((RawSimData)data.dwData[0]).ToSimState(_timeBase + _connected.Elapsed, _pause);
                _groundFt = state.AltitudeMslFt - state.AltitudeAglFt;
                StateReceived?.Invoke(state);
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

    private void ApplyArrows()
    {
        IReadOnlyList<ArrowPose> wanted;
        lock (_arrowLock)
        {
            if (!_arrowsChanged || _arrows is null) return;
            wanted = _wantedArrows;
            _arrowsChanged = false;
        }
        _arrows.Show(wanted, _groundFt);
    }

    /// <summary>A lost connection never leaves the microphone open.</summary>
    private void ReleasePushToTalk()
    {
        bool was = PushToTalkHeld;
        _holdDown = _toggleOn = false;
        if (was) PushToTalkChanged?.Invoke(false, PushToTalkSource.SimBinding);
    }
}
