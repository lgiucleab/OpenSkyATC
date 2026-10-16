using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Core.Events;

/// <summary>
/// Turns consecutive <see cref="SimState"/> snapshots into named events, with edge detection, hysteresis
/// and hold times so noise near a threshold does not fire twice. Phases react to these events and never
/// read raw numbers.
/// </summary>
/// <remarks>
/// Hold times run on an active clock that stops while the sim is paused. A position jump over
/// <see cref="EventEngineOptions.TeleportNm"/> in one tick, or the end of slew, raises <see cref="Teleported"/>;
/// nothing else fires until the position has been stable for the settle time, and every detector then
/// starts again from a fresh baseline. The first value any detector sees is its baseline and never fires.
/// </remarks>
public sealed class EventEngine(EventEngineOptions? options = null)
{
    private enum Side { Unknown, Inside, Outside }

    private sealed class RunwayWatch(Runway runway)
    {
        public Runway Runway { get; } = runway;
        public double LengthM { get; } = runway.LengthM;
        public Side State { get; set; }
    }

    /// <summary>Hold-short geometry in the runway's along/cross frame. "Inside" means past the line, towards the runway.</summary>
    private sealed class HoldShortWatch(HoldShortPoint point, Runway runway, double along, double cross, double dirAlong, double dirCross)
    {
        public HoldShortPoint Point { get; } = point;
        public Runway Runway { get; } = runway;
        public double Along { get; } = along;
        public double Cross { get; } = cross;
        public double DirAlong { get; } = dirAlong;
        public double DirCross { get; } = dirCross;
        public Side State { get; set; }
    }

    private readonly EventEngineOptions _o = options ?? new EventEngineOptions();
    private readonly List<EventRegistration> _registrations = [];
    private readonly List<RunwayWatch> _runways = [];
    private readonly List<HoldShortWatch> _holdShorts = [];
    private readonly List<StableValue<bool>> _engines = [];

    private HeldTrigger? _taxi, _airborne, _touchdown, _takeoffRoll, _levelOff;
    private StableValue<int>? _com1, _com2, _squawk;
    private StableValue<TransponderMode>? _mode;

    private TimeSpan _now;
    private TimeSpan? _lastTime;
    private GeoPoint? _lastPosition;
    private bool _paused;
    private bool _slewing;
    private GeoPoint _slewStart;
    private TimeSpan? _settleStart;

    public EventEngineOptions Options => _o;

    /// <summary>Time that has passed while the sim was not paused.</summary>
    public TimeSpan ActiveTime => _now;

    /// <summary>True between a teleport and <see cref="PositionSettled"/>.</summary>
    public bool IsSettling => _settleStart is not null;

    /// <summary>The runway the aircraft is on, while on the ground; otherwise null.</summary>
    public Runway? CurrentRunway { get; private set; }

    public IReadOnlyList<EventRegistration> Registrations => _registrations;

    /// <summary>Sets the airports whose runways and hold-short lines are watched (origin, destination, alternate).</summary>
    public void SetAirports(params AirportData[] airports)
    {
        _runways.Clear();
        _holdShorts.Clear();
        CurrentRunway = null;
        foreach (var airport in airports)
        {
            foreach (var runway in airport.Runways) _runways.Add(new RunwayWatch(runway));
            foreach (var hs in airport.HoldShorts)
            {
                if (airport.FindRunway(hs.RunwayDesignator) is not { } runway) continue;
                var (a, c) = runway.Project(hs.Position);
                double footAlong = Math.Clamp(a, 0, runway.LengthM);
                double da = footAlong - a, dc = -c, norm = Math.Sqrt(da * da + dc * dc);
                if (norm < 1) continue; // point lies on the runway itself: no direction to cross
                _holdShorts.Add(new HoldShortWatch(hs, runway, a, c, da / norm, dc / norm));
            }
        }
    }

    public AltitudeRegistration RegisterAltitude(object owner, double feet, AltitudeReference reference = AltitudeReference.Indicated, string? tag = null) =>
        Add(new AltitudeRegistration(owner, feet, reference, _o.AltitudeBandFt, tag));

    public DistanceRegistration RegisterDistance(object owner, GeoPoint point, double nm, string? tag = null) =>
        Add(new DistanceRegistration(owner, point, nm, _o.DistanceBandNm, tag));

    /// <param name="courseTrueDeg">Final approach course; defaults to the runway end's heading.</param>
    /// <param name="maxDistanceNm">Ignore the course beyond this distance from the runway end; null for no limit.</param>
    public EstablishedRegistration RegisterEstablished(object owner, RunwayEnd runwayEnd, double? courseTrueDeg = null, double? maxDistanceNm = null, string? tag = null) =>
        Add(new EstablishedRegistration(owner, runwayEnd, courseTrueDeg ?? runwayEnd.HeadingTrueDeg, maxDistanceNm, _o, tag));

    public void Unregister(EventRegistration registration) => _registrations.Remove(registration);

    public void UnregisterAll(object owner) => _registrations.RemoveAll(r => ReferenceEquals(r.Owner, owner));

    /// <summary>Processes one snapshot and returns the events it fired, in a fixed order.</summary>
    public IReadOnlyList<SimEvent> Process(SimState s)
    {
        var events = new List<SimEvent>();
        var pos = new GeoPoint(s.Latitude, s.Longitude);

        if (s.IsPaused)
        {
            if (!_paused)
            {
                _paused = true;
                events.Add(new SimPaused(s.Time));
            }
            _lastTime = s.Time;
            return events;
        }
        if (_paused)
        {
            _paused = false;
            events.Add(new SimResumed(s.Time));
        }

        if (_lastTime is { } last && s.Time > last) _now += s.Time - last;
        _lastTime = s.Time;

        if (s.SlewActive)
        {
            if (!_slewing)
            {
                _slewing = true;
                _slewStart = _lastPosition ?? pos;
            }
            _lastPosition = pos;
            ResetDetectors();
            return events;
        }
        if (_slewing)
        {
            _slewing = false;
            events.Add(new Teleported(s.Time, GeoMath.DistanceNm(_slewStart, pos), TeleportReason.Slew));
            BeginSettle();
        }
        else if (_lastPosition is { } lp && GeoMath.DistanceNm(lp, pos) is var jump && jump > _o.TeleportNm)
        {
            events.Add(new Teleported(s.Time, jump, TeleportReason.Jump));
            BeginSettle();
        }
        _lastPosition = pos;

        if (_settleStart is { } start)
        {
            if (_now - start < _o.SettleTime) return events;
            _settleStart = null;
            events.Add(new PositionSettled(s.Time));
        }

        Detect(s, pos, events);
        return events;
    }

    private void BeginSettle()
    {
        _settleStart = _now;
        ResetDetectors();
    }

    private void ResetDetectors()
    {
        _engines.Clear();
        _taxi = _airborne = _touchdown = _takeoffRoll = _levelOff = null;
        _com1 = _com2 = _squawk = null;
        _mode = null;
        foreach (var r in _runways) r.State = Side.Unknown;
        foreach (var h in _holdShorts) h.State = Side.Unknown;
        foreach (var r in _registrations) r.Reset();
        CurrentRunway = null;
    }

    private void Detect(SimState s, GeoPoint pos, List<SimEvent> events)
    {
        var t = s.Time;
        DetectEngines(s, events);

        _taxi ??= new HeldTrigger(_o.TaxiStartHold, _o.TaxiRearmHold);
        if (_taxi.Update(s.OnGround && s.GroundSpeedKt > _o.TaxiStartKt, s.OnGround && s.GroundSpeedKt < _o.TaxiRearmKt, _now))
            events.Add(new TaxiStarted(t));

        _airborne ??= new HeldTrigger(_o.AirborneHold, _o.AirborneRearm);
        if (_airborne.Update(!s.OnGround, s.OnGround, _now))
            events.Add(new Airborne(t, ToSimTime(t, _airborne.LastFireStart)));

        _touchdown ??= new HeldTrigger(_o.TouchdownHold, _o.TouchdownRearm);
        if (_touchdown.Update(s.OnGround, !s.OnGround, _now))
            events.Add(new Touchdown(t, ToSimTime(t, _touchdown.LastFireStart)));

        DetectRunways(s, pos, events);

        _takeoffRoll ??= new HeldTrigger(TimeSpan.Zero, _o.TakeoffRollRearmHold);
        if (_takeoffRoll.Update(s.OnGround && CurrentRunway is not null && s.GroundSpeedKt > _o.TakeoffRollKt,
                s.GroundSpeedKt < _o.TakeoffRollRearmKt, _now))
            events.Add(new TakeoffRoll(t, CurrentRunway!));

        DetectHoldShorts(s, pos, events);

        _levelOff ??= new HeldTrigger(_o.LevelOffHold, _o.LevelOffRearmHold);
        double vs = Math.Abs(s.VerticalSpeedFpm);
        if (_levelOff.Update(!s.OnGround && vs <= _o.LevelOffFpm, !s.OnGround && vs > _o.LevelOffRearmFpm, _now))
            events.Add(new LevelOff(t, s.IndicatedAltitudeFt));

        DetectRegistrations(s, pos, events);

        _com1 ??= new StableValue<int>(_o.FrequencyHold);
        if (_com1.Update(s.Com1ActiveKhz, _now)) events.Add(new FrequencyChanged(t, 1, _com1.Value));
        _com2 ??= new StableValue<int>(_o.FrequencyHold);
        if (_com2.Update(s.Com2ActiveKhz, _now)) events.Add(new FrequencyChanged(t, 2, _com2.Value));

        _squawk ??= new StableValue<int>(_o.TransponderHold);
        if (_squawk.Update(s.TransponderCode, _now)) events.Add(new SquawkChanged(t, _squawk.Value));
        _mode ??= new StableValue<TransponderMode>(_o.TransponderHold);
        if (_mode.Update(s.TransponderMode, _now)) events.Add(new TransponderModeChanged(t, _mode.Value));
    }

    private void DetectEngines(SimState s, List<SimEvent> events)
    {
        var changed = new List<int>();
        for (int i = 0; i < s.EnginesRunning.Count; i++)
        {
            if (i == _engines.Count) _engines.Add(new StableValue<bool>(_o.EngineHold));
            if (_engines[i].Update(s.EnginesRunning[i], _now)) changed.Add(i);
        }
        if (changed.Count == 0) return;

        int running = _engines.Take(s.EnginesRunning.Count).Count(e => e.Value);
        foreach (int i in changed)
            events.Add(_engines[i].Value ? new EnginesStarted(s.Time, i + 1, running) : new EnginesStopped(s.Time, i + 1, running));
    }

    private void DetectRunways(SimState s, GeoPoint pos, List<SimEvent> events)
    {
        CurrentRunway = null;
        foreach (var w in _runways)
        {
            if (!s.OnGround)
            {
                w.State = Side.Unknown; // the polygon is for ground movement; overflights do not count
                continue;
            }

            var (along, cross) = w.Runway.Project(pos);
            double half = w.Runway.WidthM / 2, m = _o.RunwayMarginM;
            bool inside = along >= 0 && along <= w.LengthM && Math.Abs(cross) <= half;
            bool outside = along < -m || along > w.LengthM + m || Math.Abs(cross) > half + m;

            switch (w.State)
            {
                case Side.Unknown when inside: w.State = Side.Inside; break;
                case Side.Unknown when outside: w.State = Side.Outside; break;
                case Side.Outside when inside:
                    w.State = Side.Inside;
                    events.Add(new EnteredRunway(s.Time, w.Runway));
                    break;
                case Side.Inside when outside:
                    w.State = Side.Outside;
                    events.Add(new ExitedRunway(s.Time, w.Runway));
                    break;
            }
            if (w.State == Side.Inside) CurrentRunway ??= w.Runway;
        }
    }

    private void DetectHoldShorts(SimState s, GeoPoint pos, List<SimEvent> events)
    {
        foreach (var h in _holdShorts)
        {
            var (pa, pc) = h.Runway.Project(pos);
            double ra = pa - h.Along, rc = pc - h.Cross;
            double along = ra * h.DirAlong + rc * h.DirCross;
            double lateral = Math.Abs(ra * h.DirCross - rc * h.DirAlong);

            if (!s.OnGround || lateral > _o.HoldShortHalfWidthM)
            {
                h.State = Side.Unknown;
                continue;
            }

            double m = _o.HoldShortMarginM;
            switch (h.State)
            {
                case Side.Unknown when along < -m: h.State = Side.Outside; break;
                case Side.Unknown when along > m: h.State = Side.Inside; break;
                case Side.Outside when along > m:
                    h.State = Side.Inside;
                    events.Add(new PassedHoldShort(s.Time, h.Runway, h.Point));
                    break;
                case Side.Inside when along < -m: h.State = Side.Outside; break;
            }
        }
    }

    private void DetectRegistrations(SimState s, GeoPoint pos, List<SimEvent> events)
    {
        foreach (var reg in _registrations)
        {
            switch (reg)
            {
                case AltitudeRegistration a:
                    double alt = a.Reference == AltitudeReference.Indicated ? s.IndicatedAltitudeFt : s.AltitudeAglFt;
                    int dir = a.Crossing.Update(alt);
                    if (dir != 0) events.Add(new AltitudeCrossed(s.Time, a, Climbing: dir > 0));
                    break;

                case DistanceRegistration d:
                    int ddir = d.Crossing.Update(GeoMath.DistanceNm(pos, d.Point));
                    if (ddir != 0) events.Add(new DistanceCrossed(s.Time, d, Inbound: ddir < 0));
                    break;

                case EstablishedRegistration e:
                    double distM = GeoMath.DistanceM(e.RunwayEnd.Position, pos);
                    double expectedBearing = GeoMath.NormalizeDeg(e.CourseTrueDeg + 180);
                    double dev = Math.Abs(GeoMath.AngleDiffDeg(GeoMath.BearingDeg(e.RunwayEnd.Position, pos), expectedBearing));
                    bool closing = e.PreviousDistanceM is { } prev && distM < prev;
                    e.PreviousDistanceM = distM;
                    bool inRange = e.MaxDistanceNm is not { } max || distM / GeoMath.MetresPerNm <= max;
                    if (e.Trigger.Update(!s.OnGround && dev <= _o.EstablishedDeg && closing && inRange, dev > _o.EstablishedReleaseDeg, _now))
                        events.Add(new Established(s.Time, e));
                    break;
            }
        }
    }

    private T Add<T>(T registration) where T : EventRegistration
    {
        _registrations.Add(registration);
        return registration;
    }

    /// <summary>Converts an active-clock instant back to sim time, relative to the current tick.</summary>
    private TimeSpan ToSimTime(TimeSpan simNow, TimeSpan activeInstant) => simNow - (_now - activeInstant);
}
