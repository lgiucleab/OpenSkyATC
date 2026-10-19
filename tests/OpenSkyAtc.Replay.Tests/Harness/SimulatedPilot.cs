using OpenSkyAtc.Core.Brain;
using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Sim;
using OpenSkyAtc.Phraseology;

namespace OpenSkyAtc.Replay.Tests.Harness;

public sealed record SimulatedPilotOptions
{
    /// <summary>Go around at 200 ft on the first approach (after the landing clearance).</summary>
    public bool GoAroundOnFirstApproach { get; init; }

    /// <summary>Report "going around" instead of letting Tower detect it.</summary>
    public bool ReportGoAround { get; init; }

    public TimeSpan ReadbackDelay { get; init; } = TimeSpan.FromSeconds(4);
    public TimeSpan TuneDelay { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan CallDelay { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromHours(3);

    /// <summary>The plan to fly; defaults to <see cref="SyntheticRoute.Plan"/> (straight in).</summary>
    public FlightPlan? Plan { get; init; }

    /// <summary>The destination; defaults to <see cref="SyntheticRoute.Destination"/>.</summary>
    public AirportData? Destination { get; init; }
    public PhraseologyOptions? Phraseology { get; init; }

    // ---- Known errors, for the debrief (step 16). Each happens once.

    /// <summary>Reads back the first squawk wrongly.</summary>
    public bool WrongSquawkReadback { get; init; }

    /// <summary>Reads back the first frequency change this late (past the missed-readback time).</summary>
    public TimeSpan? LateFrequencyReadback { get; init; }

    /// <summary>Changes to the first airborne handoff frequency this late.</summary>
    public TimeSpan? LateAirborneHandoff { get; init; }

    /// <summary>Climbs this far past the first level-off and stays there for 45 s.</summary>
    public double AltitudeOvershootFt { get; init; }

    /// <summary>Records the flight as the app does (flight log), when set.</summary>
    public Core.Recording.FlightRecorder? Recorder { get; init; }

    /// <summary>The start line for <see cref="Recorder"/>: the airports given here stand in for the sim's data.</summary>
    public Func<Core.Model.FlightPlan, int, Core.Recording.FlightLogStart>? RecorderStart { get; init; }
}

/// <summary>
/// The design doc's simulated pilot: a closed-loop bot that flies the <see cref="Clearance"/> (snapping
/// towards cleared altitudes, headings and the route), reads back every instruction, changes frequency after
/// handoffs and makes the expected calls. Simple kinematics on <see cref="SyntheticRoute"/>; deterministic.
/// </summary>
public sealed class SimulatedPilot(SimulatedPilotOptions? options = null)
{
    private enum Mode { AtGate, Pushing, WaitTaxi, TaxiOut, AtHoldingPoint, LiningUp, LinedUp, TakeoffRoll, Airborne, Rollout, Vacating, WaitTaxiIn, TaxiIn, Parked, Shutdown }

    private static readonly TimeSpan Dt = TimeSpan.FromMilliseconds(250);
    private const double GlideFtPerNm = 318; // 3 degrees

    private readonly SimulatedPilotOptions _o = options ?? new SimulatedPilotOptions();
    private AtcBrain _brain = null!;

    private Mode _mode = Mode.AtGate;
    private TimeSpan _t, _modeSince;
    private GeoPoint _pos = SyntheticRoute.OriginStand;
    private double _altFt, _hdg = 180, _ias, _vs, _rolledM;
    private bool _onGround = true, _brake = true;
    private readonly bool[] _engines = [false, false];
    private int _com1 = SyntheticRoute.OriginDeliveryKhz;

    private string? _readbackKey;
    private TimeSpan? _readbackAt;
    private ControllerUnit? _tuneTo;
    private TimeSpan? _tuneAt, _callAt;
    private bool _requestedClearance, _requestedPush, _requestedTaxi, _calledGround;
    private int _waypoint;
    private bool _onFinal, _passedIntermediate, _didGoAround, _goAroundClimbing, _waitingNewApproach;
    private bool _wrongSquawkDone, _lateReadbackDone, _lateHandoffDone, _overshootDone;
    private TimeSpan _overshootUntil;

    public AtcBrain Brain => _brain;

    /// <summary>Flies the whole route and returns the transcript.</summary>
    public IReadOnlyList<string> Fly(int seed, AtcBrainOptions? brainOptions = null)
    {
        _brain = new AtcBrain((brainOptions ?? new AtcBrainOptions()) with { CenterFrequencies = SyntheticRoute.Centers });
        _brain.Start(PlanToFly, SyntheticRoute.Origin, DestinationToFly, seed);
        if (_o.Recorder is { } recorder && _o.RecorderStart is { } start) recorder.Begin(start(PlanToFly, seed), _brain);

        while (!_brain.Session.Ended && _t < _o.MaxDuration)
        {
            var state = State();
            _brain.Tick(state, _t);
            _o.Recorder?.Ticked(state);
            Radio();
            Move();
            _t += Dt;
        }
        return ReplayRunner.Transcript(_brain.Session, new IcaoRenderer(seed, _o.Phraseology));
    }

    /// <summary>A pilot call to the brain (and the flight log).</summary>
    private void Call(PilotIntent intent)
    {
        _o.Recorder?.Intent(intent);
        _brain.HandlePilotIntent(intent);
    }

    private Clearance C => _brain.Session.Clearance;
    private FlightPlan PlanToFly => _o.Plan ?? SyntheticRoute.Plan;
    private AirportData DestinationToFly => _o.Destination ?? SyntheticRoute.Destination;
    private FlightPhase Phase => _brain.Session.Phase;

    private SimState State() => new()
    {
        Time = _t,
        Latitude = _pos.Latitude,
        Longitude = _pos.Longitude,
        AltitudeMslFt = _altFt,
        AltitudeAglFt = _altFt,
        IndicatedAltitudeFt = _altFt,
        IndicatedAirspeedKt = _ias,
        GroundSpeedKt = GroundSpeed(),
        HeadingMagneticDeg = _hdg,
        HeadingTrueDeg = _hdg,
        VerticalSpeedFpm = _vs,
        OnGround = _onGround,
        ParkingBrakeSet = _brake,
        EnginesRunning = [.. _engines],
        Com1ActiveKhz = _com1,
        Com2ActiveKhz = SyntheticRoute.GuardKhz,
        Com1Transmit = true,
        TransponderCode = C.Squawk ?? 2000,
        TransponderMode = _onGround ? TransponderMode.Standby : TransponderMode.Alt,
        WindDirectionTrueDeg = 90,
        WindSpeedKt = 10,
        VisibilityM = 10_000,
        TemperatureC = 15,
        SeaLevelPressureHpa = 1013.4,
    };

    private double GroundSpeed() => _onGround ? _ias : _ias * (1 + _altFt / 1000 * 0.015);

    // ---- Radio: readbacks, frequency changes and calls.

    private void Radio()
    {
        var available = _brain.AvailableIntents;
        var readback = available.OfType<Readback>().FirstOrDefault();
        string? key = readback is null ? null : $"{readback.ExchangeId}:{string.Join(",", readback.Items.Keys)}";
        if (key != _readbackKey)
        {
            _readbackKey = key;
            var delay = _o.ReadbackDelay;
            if (_o.LateFrequencyReadback is { } late && !_lateReadbackDone && readback?.Items.ContainsKey(ReadbackItem.Frequency) == true)
            {
                _lateReadbackDone = true;
                delay = late;
            }
            _readbackAt = key is null ? null : _t + delay;
        }
        else if (_readbackAt is { } at && at <= _t && readback is not null)
        {
            if (_o.WrongSquawkReadback && !_wrongSquawkDone && readback.Items.TryGetValue(ReadbackItem.Squawk, out var squawk))
            {
                _wrongSquawkDone = true;
                var items = readback.Items.ToDictionary(kv => kv.Key, kv => kv.Value);
                items[ReadbackItem.Squawk] = squawk == "1234" ? "4321" : "1234";
                readback = readback with { Items = items };
            }
            Call(readback);
            _readbackAt = null;
        }

        if (_callAt is { } call && call <= _t) MakeCheckInCall(available);

        switch (Phase)
        {
            case FlightPhase.Preflight when !_requestedClearance && _t >= TimeSpan.FromSeconds(10):
                _requestedClearance = true;
                Call(new RequestClearance());
                break;
            case FlightPhase.Cleared when !_requestedPush && _com1 == SyntheticRoute.OriginGroundKhz && readback is null:
                _requestedPush = true;
                Call(new RequestPushStart());
                break;
            case FlightPhase.Pushback when !_requestedTaxi && _mode == Mode.WaitTaxi:
                _requestedTaxi = true;
                Call(new RequestTaxi());
                break;
        }

        if (_mode == Mode.WaitTaxiIn && !_calledGround && _com1 == SyntheticRoute.DestGroundKhz && readback is null)
        {
            _calledGround = true;
            Call(new CheckIn());
        }

        // Change frequency last, so every call above goes out on the frequency the brain saw this tick.
        if (C.HandoffTo is { } to && readback is null && _tuneTo != to)
        {
            _tuneTo = to;
            _tuneAt = _t + _o.TuneDelay;
            if (_o.LateAirborneHandoff is { } late && !_lateHandoffDone && !_onGround)
            {
                _lateHandoffDone = true;
                _tuneAt = _t + late;
            }
        }
        if (_tuneAt is { } tune && tune <= _t && _tuneTo is not null)
        {
            _com1 = _tuneTo.FrequencyKhz;
            _tuneAt = null;
            _callAt = _t + _o.CallDelay;
        }
    }

    private void MakeCheckInCall(IReadOnlyList<PilotIntent> available)
    {
        switch (Phase)
        {
            case FlightPhase.Preflight or FlightPhase.Cleared or FlightPhase.Pushback:
                break; // the push request is the first call on Ground
            case FlightPhase.LandingRoll or FlightPhase.TaxiIn or FlightPhase.Parked:
                break; // Ground is called once vacated (see Radio)
            case FlightPhase.TaxiOut:
                Call(new ReadyForDeparture());
                break;
            default:
                Call(available.OfType<CheckIn>().FirstOrDefault()
                    ?? new CheckIn((int)(Math.Round(_altFt / 100) * 100), C.ClearedAltitude));
                break;
        }
        _callAt = null;
    }

    // ---- Movement.

    private void SetMode(Mode mode)
    {
        _mode = mode;
        _modeSince = _t;
    }

    private double InMode => (_t - _modeSince).TotalSeconds;

    private void Move()
    {
        double dt = Dt.TotalSeconds;
        switch (_mode)
        {
            case Mode.AtGate:
                if (Phase == FlightPhase.Pushback) { SetMode(Mode.Pushing); _brake = false; }
                break;
            case Mode.Pushing:
                if (InMode >= 10) _engines[0] = true;
                if (InMode >= 15) _engines[1] = true;
                _ias = 2;
                Advance(GeoMath.NormalizeDeg(_hdg + 180), dt); // backwards
                if (InMode >= 30) { _ias = 0; SetMode(Mode.WaitTaxi); }
                break;
            case Mode.WaitTaxi:
                if (C.Taxi is { Runway: not null }) SetMode(Mode.TaxiOut);
                break;
            case Mode.TaxiOut:
                if (DriveTo(SyntheticRoute.HoldingPoint(SyntheticRoute.OriginThreshold), 15, dt)) SetMode(Mode.AtHoldingPoint);
                break;
            case Mode.AtHoldingPoint:
                if (C.LineUpCleared is not null || C.TakeoffCleared is not null) SetMode(Mode.LiningUp);
                break;
            case Mode.LiningUp:
                if (DriveTo(GeoMath.Destination(SyntheticRoute.OriginThreshold, 90, 50), 5, dt)) { _hdg = 90; SetMode(Mode.LinedUp); }
                break;
            case Mode.LinedUp:
                if (C.TakeoffCleared is not null) SetMode(Mode.TakeoffRoll);
                break;
            case Mode.TakeoffRoll:
                _ias = Math.Min(160, _ias + 10 * dt);
                Advance(_hdg, dt);
                if (_ias >= 150) { _onGround = false; SetMode(Mode.Airborne); }
                break;
            case Mode.Airborne:
                FlyAirborne(dt);
                break;
            case Mode.Rollout:
                _ias = Math.Max(15, _ias - 4 * dt);
                _rolledM += Advance(_hdg, dt);
                if (_ias <= 20 && _rolledM >= 1000) { _hdg = 0; SetMode(Mode.Vacating); }
                break;
            case Mode.Vacating:
                _ias = 15;
                Advance(_hdg, dt);
                if (InMode >= 12) { _ias = 0; SetMode(Mode.WaitTaxiIn); }
                break;
            case Mode.WaitTaxiIn:
                if (C.Taxi is { Runway: null }) SetMode(Mode.TaxiIn);
                break;
            case Mode.TaxiIn:
                if (DriveTo(SyntheticRoute.DestinationStand, 15, dt)) { _brake = true; SetMode(Mode.Parked); }
                break;
            case Mode.Parked:
                if (InMode >= 15) { _engines[0] = _engines[1] = false; SetMode(Mode.Shutdown); }
                break;
        }
    }

    private void FlyAirborne(double dt)
    {
        var runway = C.Runway is { } r && Phase >= FlightPhase.Descent ? DestinationToFly.FindRunway(r)?.End(r) : null;
        double toThresholdNm = runway is null ? double.MaxValue : GeoMath.DistanceNm(_pos, runway.Position);
        // Distance to the threshold along the final course: negative once past it.
        double alongFinalNm = runway is null ? double.MaxValue
            : toThresholdNm * Math.Cos(GeoMath.AngleDiffDeg(GeoMath.BearingDeg(runway.Position, _pos), runway.HeadingTrueDeg + 180) * Math.PI / 180);

        // Localiser capture: cleared for the approach, near the centreline, roughly on course.
        if (!_onFinal && !_goAroundClimbing && C.Approach is not null && runway is not null && alongFinalNm > 0)
        {
            var far = GeoMath.Destination(runway.Position, runway.HeadingTrueDeg + 180, 30 * GeoMath.MetresPerNm);
            if (Math.Abs(GeoMath.TrackOffsets(far, runway.Position, _pos).CrossNm) < 0.3
                && Math.Abs(GeoMath.AngleDiffDeg(_hdg, runway.HeadingTrueDeg)) < 45) _onFinal = true;
        }

        // Lateral.
        double target;
        if (_goAroundClimbing) target = 90;
        else if (_onFinal && runway is not null)
        {
            var far = GeoMath.Destination(runway.Position, runway.HeadingTrueDeg + 180, 30 * GeoMath.MetresPerNm);
            target = runway.HeadingTrueDeg - Math.Clamp(GeoMath.TrackOffsets(far, runway.Position, _pos).CrossNm * 60, -30, 30);
        }
        else if (C.AssignedHeadingDeg is { } hdg) target = hdg;
        else if (C.Approach is not null && runway is not null && !_waitingNewApproach)
        {
            // Own navigation: an intermediate point 14 nm out, then the final approach fix 10 nm out, then the runway.
            var faf = GeoMath.Destination(runway.Position, runway.HeadingTrueDeg + 180, 10 * GeoMath.MetresPerNm);
            var intermediate = GeoMath.Destination(runway.Position, runway.HeadingTrueDeg + 180, 14 * GeoMath.MetresPerNm);
            if (!_passedIntermediate && GeoMath.DistanceNm(_pos, intermediate) < 1.5) _passedIntermediate = true;
            if (_passedIntermediate && !_onFinal && GeoMath.DistanceNm(_pos, faf) < 1.5) _onFinal = true;
            // On final: the runway course, corrected by the cross-track distance from the extended centreline.
            target = _onFinal
                ? runway.HeadingTrueDeg - Math.Clamp(GeoMath.TrackOffsets(faf, runway.Position, _pos).CrossNm * 60, -30, 30)
                : GeoMath.BearingDeg(_pos, _passedIntermediate ? faf : intermediate);
        }
        else target = RouteBearing();
        double turn = Math.Clamp(GeoMath.AngleDiffDeg(target, _hdg), -3 * dt, 3 * dt);
        _hdg = GeoMath.NormalizeDeg(_hdg + turn);

        // Vertical.
        double targetFt = _goAroundClimbing ? 5000 : C.ClearedAltitude?.NominalFeet ?? _altFt;
        if (_o.AltitudeOvershootFt > 0 && !_overshootDone && Phase >= FlightPhase.Departure && C.ClearedAltitude is { } cleared
            && _vs > 0 && Math.Abs(_altFt - cleared.NominalFeet) < 50)
        {
            _overshootDone = true;
            _overshootUntil = _t + TimeSpan.FromSeconds(45);
        }
        if (_t < _overshootUntil && C.ClearedAltitude is { } bust) targetFt = bust.NominalFeet + _o.AltitudeOvershootFt;
        if (_onFinal && !_goAroundClimbing) targetFt = Math.Min(targetFt, alongFinalNm * GlideFtPerNm - 50); // aim just past the threshold
        double rate = targetFt > _altFt ? 2000 : 2500; // fpm
        double step = Math.Clamp(targetFt - _altFt, -rate / 60 * dt, rate / 60 * dt);
        if (_onFinal && !_goAroundClimbing && step < 0) step = Math.Max(targetFt - _altFt, -1200 / 60.0 * dt); // follow the glide
        _altFt += step;
        _vs = step / dt * 60;

        // Speed.
        double ias = C.SpeedRestrictionKt is { } restricted ? restricted
            : _onFinal && toThresholdNm < 8 ? 140 : toThresholdNm < 25 ? 200 : _altFt < 10_000 ? 250 : 290;
        _ias += Math.Clamp(ias - _ias, -2 * dt, 2 * dt);
        Advance(_hdg, dt);

        if (_goAroundClimbing && C.AssignedHeadingDeg is not null) _goAroundClimbing = false; // ATC's missed-approach instruction is in
        if (_waitingNewApproach && !_goAroundClimbing && C.Approach is not null && C.AssignedHeadingDeg is null) _waitingNewApproach = false;

        if (_onFinal && !_didGoAround && _o.GoAroundOnFirstApproach && C.LandingCleared is not null && _altFt < 200)
        {
            _didGoAround = true;
            _goAroundClimbing = true;
            _onFinal = false;
            _passedIntermediate = false;
            _waitingNewApproach = true;
            if (_o.ReportGoAround) Call(new GoingAround());
            return;
        }

        if (_onFinal && _altFt <= 0)
        {
            _altFt = 0;
            _vs = 0;
            _onGround = true;
            _hdg = runway?.HeadingTrueDeg ?? _hdg;
            SetMode(Mode.Rollout);
        }
    }

    /// <summary>Bearing along the planned route: the next waypoint, then the destination threshold.</summary>
    private double RouteBearing()
    {
        var points = PlanToFly.Route.Select(w => w.Position).Append(SyntheticRoute.DestinationThreshold).ToList();
        while (_waypoint < points.Count - 1 && GeoMath.DistanceNm(_pos, points[_waypoint]) < 3) _waypoint++;
        return GeoMath.BearingDeg(_pos, points[_waypoint]);
    }

    /// <summary>Moves towards a point at a taxi speed; true when there.</summary>
    private bool DriveTo(GeoPoint target, double kt, double dt)
    {
        double metres = GeoMath.DistanceM(_pos, target);
        if (metres < 5)
        {
            _ias = 0;
            return true;
        }
        _hdg = GeoMath.BearingDeg(_pos, target);
        _ias = kt;
        double step = Math.Min(metres, kt * GeoMath.MetresPerNm / 3600 * dt);
        _pos = GeoMath.Destination(_pos, _hdg, step);
        return false;
    }

    private double Advance(double bearing, double dt)
    {
        double metres = GroundSpeed() * GeoMath.MetresPerNm / 3600 * dt;
        _pos = GeoMath.Destination(_pos, bearing, metres);
        return metres;
    }
}
