using OpenSkyAtc.Core.Airspace;
using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Events;
using OpenSkyAtc.Core.Messages;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Phases;
using OpenSkyAtc.Core.Planning;
using OpenSkyAtc.Core.Radio;
using OpenSkyAtc.Core.Random;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Core.Brain;

public sealed record AtcBrainOptions
{
    public EventEngineOptions Events { get; init; } = new();
    public ConversationTiming Timing { get; init; } = new();
    public ITaxiPlanner TaxiPlanner { get; init; } = new FallbackTaxiPlanner();
    public SquawkConfig Squawks { get; init; } = SquawkConfig.Empty;

    /// <summary>Chance that Tower says "line up and wait" before the takeoff clearance (decision 7, step 3).</summary>
    public double LineUpAndWaitProbability { get; init; } = 0.3;

    public (double Min, double Max) LineUpToTakeoffSeconds { get; init; } = (20, 60);
}

/// <summary>
/// The ATC brain: a deterministic, tick-driven state machine for one IFR flight. Snapshots and pilot
/// intents go in; structured <see cref="AtcMessage"/>s come out. No SimConnect, audio, UI or I/O.
/// </summary>
public sealed class AtcBrain(AtcBrainOptions? options = null) : IAtcBrain
{
    private const int MaxTransitionsPerCall = 8;

    private readonly AtcBrainOptions _options = options ?? new AtcBrainOptions();
    private readonly List<(PilotIntent Intent, ControllerUnit Unit)> _deferred = [];

    private FlightPlan _plan = null!;
    private AirportData _origin = null!;
    private AirportData _destination = null!;
    private AircraftCallsign _callsign = null!;
    private EventEngine _events = null!;
    private TransmissionQueue _queue = null!;
    private ConversationManager _conversation = null!;
    private AirspaceModel _airspace = null!;
    private IRandom _rng = null!;
    private IPhase _phase = null!;
    private bool _phaseEntered;
    private SimState? _state;

    public FlightSession Session { get; private set; } = new(0);

    public event Action<AtcMessage>? Transmission;

    public void Start(FlightPlan plan, AirportData origin, AirportData destination, int seed)
    {
        _plan = plan;
        _origin = origin;
        _destination = destination;
        _callsign = new AircraftCallsign(plan.Callsign, plan.Telephony);
        _rng = new SeededRandom(seed);
        Session = new FlightSession(seed);
        _events = new EventEngine(_options.Events);
        _events.SetAirports(origin, destination);
        _queue = new TransmissionQueue();
        _conversation = new ConversationManager(_queue, _rng, Session.Log, _options.Timing);
        _airspace = AirspaceModel.Build(origin, destination);
        _deferred.Clear();
        _state = null;
        // Step 6 infers the phase for sessions that start in the air or on the runway.
        _phase = Create(FlightPhase.Preflight);
        _phaseEntered = false;
    }

    public IReadOnlyList<PilotIntent> AvailableIntents
    {
        get
        {
            if (_state is null) return [];
            var ctx = Context([]);
            var list = new List<PilotIntent>();
            if (_conversation.PendingReadback() is { } ex)
                list.Add(new Readback(ex.Outstanding.ToDictionary(kv => kv.Key, kv => kv.Value), ex.Id));
            list.AddRange(_phase.AvailableIntents(ctx));
            list.Add(new SayAgain());
            return list;
        }
    }

    public void Tick(SimState state, TimeSpan simTime)
    {
        _state = state;
        Session.SimTime = simTime;
        var newEvents = _events.Process(state);
        foreach (var e in newEvents) Session.Log.Add(new SimEventLogged(e));

        if (!_phaseEntered)
        {
            _phaseEntered = true;
            Session.Phase = _phase.Id;
            _phase.OnEnter(Context([]));
            Session.CurrentUnit = _phase.Unit(Context([]));
        }

        // Paused or settling after a teleport: every timer and the queue are frozen.
        if (state.IsPaused || _events.IsSettling) return;

        Apply(_phase.Tick(Context(newEvents)), null, null);
        _conversation.Tick(_events.ActiveTime, simTime);
        DispatchDeferred();
        Release(state, simTime);
    }

    public void HandlePilotIntent(PilotIntent intent)
    {
        if (_state is not { } state) return;

        int khz = state.TransmitFrequencyKhz;
        var unit = _airspace.UnitOn(khz);
        Session.LastPilotTransmission = Session.SimTime;
        Session.Log.Add(new PilotIntentLogged(Session.SimTime, intent, khz, unit));
        if (unit is null) return; // nobody on this frequency: silence, as in reality

        Dispatch(intent, unit, allowDefer: true);
        Release(state, Session.SimTime);
    }

    private void Dispatch(PilotIntent intent, ControllerUnit unit, bool allowDefer)
    {
        var now = _events.ActiveTime;
        switch (intent)
        {
            case Readback r:
                _conversation.HandleReadback(unit, r.Items, r.ExchangeId, now, Session.SimTime);
                return;
            case Wilco:
                // Wilco is not a readback: if one is outstanding, every item counts as missing.
                _conversation.HandleReadback(unit, new Dictionary<ReadbackItem, string>(), null, now, Session.SimTime);
                return;
            case SayAgain:
                _conversation.SayAgain(unit, now);
                return;
            case Mayday or PanPan:
                var kind = intent is Mayday ? EmergencyKind.Mayday : EmergencyKind.PanPan;
                _conversation.Say(unit, [new EmergencyAcknowledged(kind)], TransmissionPriority.Urgent, now);
                return;
        }

        if (_conversation.IsBusy(unit))
        {
            if (allowDefer)
            {
                _conversation.Say(unit, [new StandBy()], TransmissionPriority.Reply, now + _conversation.CorrectionDelay());
                _deferred.Add((intent, unit));
            }
            return;
        }

        var result = _phase.OnPilotIntent(Context([], unit), intent);
        if (!result.Handled)
        {
            Reject(intent, unit);
            return;
        }
        Apply(result, intent, unit);
    }

    /// <summary>Requests the phase does not take: "unable, contact [right unit]" or "say again".</summary>
    private void Reject(PilotIntent intent, ControllerUnit unit)
    {
        var now = _events.ActiveTime;
        if (_phase.Unit(Context([])) is { } right && right != unit)
        {
            _conversation.Say(unit, [new UnableContact(right.Callsign, right.FrequencyKhz)], TransmissionPriority.Reply, now + _conversation.ReplyDelay());
            Session.Log.Add(new RequestRejected(Session.SimTime, intent, $"wrong unit, {right.Callsign} handles {_phase.Id}"));
        }
        else
        {
            _conversation.Say(unit, [new SayAgainRequest()], TransmissionPriority.Reply, now + _conversation.ReplyDelay());
            Session.Log.Add(new RequestRejected(Session.SimTime, intent, $"not available in {_phase.Id}"));
        }
    }

    private void Apply(PhaseResult result, PilotIntent? intent, ControllerUnit? unit)
    {
        for (int i = 0; result.Transition is { } next; i++)
        {
            if (i >= MaxTransitionsPerCall) throw new InvalidOperationException($"Phase transition loop at {_phase.Id}.");

            var from = _phase.Id;
            _phase.OnExit(Context([]));
            _phase = Create(next);
            Session.Phase = next;
            Session.Log.Add(new PhaseChanged(Session.SimTime, from, next, result.Reason ?? ""));
            _phase.OnEnter(Context([]));
            Session.CurrentUnit = _phase.Unit(Context([]));

            result = result.Redispatch && intent is not null && unit is not null
                ? _phase.OnPilotIntent(Context([], unit), intent)
                : PhaseResult.Stay;
        }
    }

    private void DispatchDeferred()
    {
        for (int i = 0; i < _deferred.Count;)
        {
            var (intent, unit) = _deferred[i];
            if (_conversation.IsBusy(unit))
            {
                i++;
                continue;
            }
            _deferred.RemoveAt(i);
            Dispatch(intent, unit, allowDefer: false);
        }
    }

    private void Release(SimState state, TimeSpan simTime)
    {
        foreach (var t in _queue.TakeDue(_events.ActiveTime))
        {
            if (!state.IsTunedTo(t.Unit.FrequencyKhz))
            {
                _conversation.OnDropped(t);
                Session.Log.Add(new TransmissionDropped(simTime, t.Unit, t.Parts));
                continue;
            }
            var message = new AtcMessage(simTime, t.Unit, _callsign, t.Parts, t.Priority);
            t.OnSent?.Invoke();
            _conversation.OnSent(t, _events.ActiveTime);
            Session.Log.Add(new AtcTransmissionLogged(message));
            Transmission?.Invoke(message);
        }
    }

    private PhaseContext Context(IReadOnlyList<SimEvent> newEvents, ControllerUnit? calledUnit = null) =>
        new(_state!, Session, Session.Clearance, _events, _queue, _airspace, _rng)
        {
            NewEvents = newEvents,
            Conversation = _conversation,
            Plan = _plan,
            Origin = _origin,
            Destination = _destination,
            Options = _options,
            Now = _events.ActiveTime,
            CalledUnit = calledUnit,
        };

    private static IPhase Create(FlightPhase phase) => phase switch
    {
        FlightPhase.Preflight => new PreflightPhase(),
        FlightPhase.Cleared => new ClearedPhase(),
        FlightPhase.Pushback => new PushbackPhase(),
        FlightPhase.TaxiOut => new TaxiOutPhase(),
        FlightPhase.Holding => new HoldingPhase(),
        FlightPhase.TakeoffRoll => new TakeoffRollPhase(),
        FlightPhase.InitialClimb => new InitialClimbPhase(),
        FlightPhase.Departure => new DeparturePhase(),
        _ => throw new NotSupportedException($"Phase {phase} is built in step 6."),
    };
}
