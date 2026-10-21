using OpenSkyAtc.Core.Airspace;
using OpenSkyAtc.Core.Atis;
using OpenSkyAtc.Core.Conformance;
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
    public ITaxiPlanner TaxiPlanner { get; init; } = new GraphTaxiPlanner();
    public SquawkConfig Squawks { get; init; } = SquawkConfig.Empty;
    public SopConfig Sops { get; init; } = SopConfig.Empty;
    public ConformanceOptions Conformance { get; init; } = new();
    public AirspaceOptions Airspace { get; init; } = new();
    public PlanningOptions Planning { get; init; } = new();
    public AtisOptions Atis { get; init; } = new();

    /// <summary>CENTER frequencies from the sim's facility data along the route (step 6, decision A). Empty: no CTR sectors.</summary>
    public IReadOnlyList<CenterFrequency> CenterFrequencies { get; init; } = [];

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
    private ConformanceMonitor _monitor = null!;
    private FlightPlanning _planning = null!;
    private AtisService _atis = null!;
    private bool _phaseEntered;
    private SimState? _state;

    public FlightSession Session { get; private set; } = new(0);

    /// <summary>Time left to read back the pending instruction before ATC repeats it (for a countdown in the UI); null when none waits.</summary>
    public TimeSpan? ReadbackTimeLeft => _state is null ? null : _conversation.ReadbackTimeLeft(_events.ActiveTime);

    /// <summary>The unit the pilot should be talking to now (for hints in the UI); null before the first tick.</summary>
    public ControllerUnit? ExpectedUnit => _state is null ? null : Session.Clearance.HandoffTo ?? _phase.Unit(Context([]));

    /// <summary>The conformance monitor, for the UI (active warnings).</summary>
    public ConformanceMonitor Conformance => _monitor;

    public event Action<AtcMessage>? Transmission;

    /// <summary>
    /// The pilot read back a frequency change correctly ("Ground 121.630"): the unit to tune, for auto-tune (owner's
    /// choice, refactor phase 2). Raised inside <see cref="HandlePilotIntent"/>.
    /// </summary>
    public event Action<ControllerUnit>? FrequencyChangeReadBack;

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
        _conversation.ContactReadBack += unit => FrequencyChangeReadBack?.Invoke(unit);
        // Airspace and planning draw from their own streams so they never shift the conversation's choices.
        _airspace = AirspaceModel.Build(origin, destination, plan, _options.CenterFrequencies, new SeededRandom(unchecked(seed ^ 0x0A1125AC)), _options.Airspace);
        _planning = new FlightPlanning(plan, origin, destination, new SeededRandom(unchecked(seed ^ 0x51A221)), _options.Planning, _options.Sops);
        _monitor = new ConformanceMonitor(_options.Conformance);
        _atis = new AtisService(_airspace.OriginAtis, origin, _airspace.DestinationAtis, destination,
            new SeededRandom(unchecked(seed ^ 0x0A715)), _options.Atis, _options.Planning.Runways);
        _deferred.Clear();
        _state = null;
        _phase = Create(FlightPhase.Preflight); // replaced by the inferred phase on the first tick
        _phaseEntered = false;
    }

    /// <summary>Every controller unit of the flight (terminal units of both airports and the CTR sectors), for the voice layer.</summary>
    public IReadOnlyList<ControllerUnit> Units => _airspace is null ? [] : _airspace.Units;

    /// <summary>The ATIS broadcast on a frequency, when the aircraft is in its range (design doc "ATIS"); null otherwise.</summary>
    public AtisReport? AtisOn(int khz) => _state is null || _atis is null ? null : _atis.On(khz, _state);

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

        if (Session.Ended) return;

        if (!_phaseEntered)
        {
            _phaseEntered = true;
            EnterInferred("session start");
        }
        else if (newEvents.OfType<PositionSettled>().Any())
        {
            EnterInferred("repositioned");
        }

        // Paused or settling after a teleport: every timer and the queue are frozen.
        if (state.IsPaused || _events.IsSettling) return;

        foreach (var station in _atis.Update(state, st => _planning.RunwayInUse(st.IsArrival, state)))
            Session.Log.Add(new AtisUpdated(simTime, station.Airport.Icao, station.Letter));

        // A handoff is complete once the new frequency is tuned (step 5, decision 7).
        if (Session.Clearance.HandoffTo is { } to && state.IsTunedTo(to))
        {
            Session.Clearance.CurrentFrequencyKhz = to.FrequencyKhz;
            Session.Clearance.ClearHandoff();
        }

        Apply(_phase.Tick(Context(newEvents)), null, null);
        _monitor.Tick(Context(newEvents));
        _conversation.Tick(_events.ActiveTime, simTime);
        DispatchDeferred();
        Session.CurrentUnit = _phase.Unit(Context([])); // the unit in control can change within a phase (CTR sectors)
        Release(state, simTime);
    }

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<AtcMessage, Exchange> _exchangeOf = new();

    public void TransmissionHeard(AtcMessage message)
    {
        if (_exchangeOf.TryGetValue(message, out var exchange)) _conversation.Heard(exchange, _events.ActiveTime);
    }

    public void HandlePilotIntent(PilotIntent intent)
    {
        if (_state is not { } state) return;

        int khz = state.TransmitFrequencyKhz;
        var unit = _airspace.UnitOn(khz);
        Session.LastPilotTransmission = Session.SimTime;
        Session.Log.Add(new PilotIntentLogged(Session.SimTime, intent, khz, unit));
        if (unit is null) return; // nobody on this frequency: silence, as in reality

        TrackHandoff(unit);
        Dispatch(intent, unit, allowDefer: true);
        Session.CurrentUnit = _phase.Unit(Context([]));
        Release(state, Session.SimTime);
    }

    /// <summary>
    /// A call to a third unit (neither the one that issued the handoff nor the one it is to) while a handoff is
    /// pending means the pilot skipped it: that unit takes the call and the skip is logged.
    /// </summary>
    private void TrackHandoff(ControllerUnit unit)
    {
        var c = Session.Clearance;
        if (c.HandoffTo is not { } expected || unit == c.HandoffFrom) return;
        if (unit != expected) Session.Log.Add(new HandoffSkipped(Session.SimTime, expected, unit));
        c.CurrentFrequencyKhz = unit.FrequencyKhz;
        c.ClearHandoff();
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
            case Unreadable:
                _conversation.Say(unit, [new SayAgainRequest()], TransmissionPriority.Reply, now + _conversation.ReplyDelay());
                return;
            case Mayday or PanPan:
                var kind = intent is Mayday ? EmergencyKind.Mayday : EmergencyKind.PanPan;
                _conversation.Say(unit, [new EmergencyAcknowledged(kind)], TransmissionPriority.Urgent, now);
                return;
        }

        // Asked again for something already given: repeat it (before "stand by", since it may be the very exchange
        // that keeps the frequency busy).
        if (GrantedRequests.Tags(intent, _phase.Id) is { } tags
            && _conversation.RepeatGrant(unit, tags, anyUnit: _phase.Unit(Context([])) == unit, now) is { } granted)
        {
            Session.Log.Add(new RequestRepeated(Session.SimTime, intent, granted.Tag!));
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
            _conversation.Say(unit, [new UnableContact(right)], TransmissionPriority.Reply, now + _conversation.ReplyDelay());
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
            if (!state.IsTunedTo(t.Unit))
            {
                _conversation.OnDropped(t);
                Session.Log.Add(new TransmissionDropped(simTime, t.Unit, t.Parts));
                continue;
            }
            var message = new AtcMessage(simTime, t.Unit, _callsign, t.Parts, t.Priority);
            t.OnSent?.Invoke();
            _conversation.OnSent(t, _events.ActiveTime);
            if (t.Exchange is { } exchange) _exchangeOf.AddOrUpdate(message, exchange);
            Session.Log.Add(new AtcTransmissionLogged(message));
            Transmission?.Invoke(message);
        }
    }

    /// <summary>Starts the phase that fits the aircraft's situation (design doc edge cases: start away from the gate, teleport).</summary>
    private void EnterInferred(string why)
    {
        var ctx = Context([]);
        var (phase, reason) = PhaseInference.Infer(ctx);
        if (why != "session start" || phase != FlightPhase.Preflight)
            Session.Log.Add(new PhaseInferred(Session.SimTime, phase, $"{why}: {reason}"));
        PhaseInference.Prepare(ctx, phase);
        if (_phaseEntered && why != "session start") _phase.OnExit(ctx);
        _phase = Create(phase);
        Session.Phase = phase;
        _phase.OnEnter(Context([]));
        Session.CurrentUnit = _phase.Unit(Context([]));
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
            Planning = _planning,
            Atis = _atis,
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
        FlightPhase.EnRoute => new EnRoutePhase(),
        FlightPhase.Descent => new DescentPhase(),
        FlightPhase.Approach => new ApproachPhase(),
        FlightPhase.Final => new FinalPhase(),
        FlightPhase.LandingRoll => new LandingRollPhase(),
        FlightPhase.TaxiIn => new TaxiInPhase(),
        FlightPhase.Parked => new ParkedPhase(),
        _ => throw new NotSupportedException($"Unknown phase {phase}."),
    };
}
