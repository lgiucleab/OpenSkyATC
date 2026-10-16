using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Events;
using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Messages;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Phases;
using OpenSkyAtc.Core.Radio;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Core.Conformance;

/// <summary>
/// Compares the aircraft with the <see cref="Clearance"/> every tick. Each check has a tolerance, a grace
/// time and three escalation levels; the level resets when the aircraft is back inside tolerance, and after
/// level 3 nothing more is said. Every warning is its own transmission. Episodes are logged for the debrief.
/// </summary>
public sealed class ConformanceMonitor
{
    private readonly record struct Reading(bool Outside, double Value = 0)
    {
        public static readonly Reading Inside = new(false);
        public static Reading Out(double value) => new(true, value);
    }

    private sealed class Episode(TimeSpan start, TimeSpan simStart)
    {
        public TimeSpan Start { get; } = start;
        public TimeSpan SimStart { get; } = simStart;
        public int Level { get; set; }
        public TimeSpan LastLevelAt { get; set; }
        public double Worst { get; set; }
    }

    /// <param name="Warn">Sends the level's transmission; returns false when it could not be sent.</param>
    private sealed record CheckDef(
        ConformanceCheck Id, TimeSpan Grace, bool Serious, bool SeriousRepeats, string Unit,
        Func<PhaseContext, Reading> Read, Func<PhaseContext, int, bool> Warn)
    {
        public Episode? Episode { get; set; }
    }

    private readonly ConformanceOptions _o;
    private readonly List<CheckDef> _checks;

    // What the monitor remembers about the clearance and the flight between ticks.
    private bool _airborne;
    private TimeSpan _airborneSince;
    private AltitudeTarget? _cleared;
    private TimeSpan _clearedAt;
    private double _altitudeAtClearance;
    private int _verticalDirection;
    private bool _complying;
    private int? _assignedSpeed;
    private TimeSpan _speedAssignedAt;
    private int? _assignedHeading;
    private TimeSpan _headingAssignedAt;
    private bool _headingArmed;
    private string? _directTo;
    private GeoPoint _directFrom;
    private bool _incursion, _incursionNew;
    private bool _unclearedTakeoff, _unclearedTakeoffNew;

    public ConformanceMonitor(ConformanceOptions? options = null)
    {
        _o = options ?? new ConformanceOptions();
        TimeSpan zero = TimeSpan.Zero;
        _checks =
        [
            new(ConformanceCheck.AltitudeBust, _o.AltitudeBustGrace, false, false, "ft", ReadAltitudeBust, WarnAltitudeBust),
            new(ConformanceCheck.AltitudeNotComplying, _o.NotComplyingGrace, false, false, "s", ReadNotComplying, WarnNotComplying),
            new(ConformanceCheck.Heading, _o.HeadingGrace, false, false, "deg", ReadHeading, WarnHeading),
            new(ConformanceCheck.OffRoute, _o.OffRouteGrace, false, false, "nm", ReadOffRoute, WarnOffRoute),
            new(ConformanceCheck.Speed, _o.SpeedGrace, false, false, "kt", ReadSpeed, WarnSpeed),
            new(ConformanceCheck.Squawk, _o.SquawkGrace, false, false, "", ReadSquawk, WarnSquawk),
            new(ConformanceCheck.MissedHandoff, _o.HandoffGrace, false, false, "s", ReadMissedHandoff, WarnMissedHandoff),
            new(ConformanceCheck.RunwayIncursion, zero, true, false, "", ReadIncursion, WarnIncursion),
            new(ConformanceCheck.TakeoffWithoutClearance, zero, true, false, "kt", ReadUnclearedTakeoff, WarnUnclearedTakeoff),
            new(ConformanceCheck.LandingWithoutClearance, zero, true, true, "ft", ReadUnclearedLanding, WarnUnclearedLanding),
            new(ConformanceCheck.TaxiOffRoute, _o.TaxiOffRouteGrace, false, false, "m", ReadTaxiOffRoute, WarnTaxiOffRoute),
        ];
    }

    /// <summary>Checks currently out of tolerance, with their level (for the UI and debrief).</summary>
    public IEnumerable<(ConformanceCheck Check, int Level)> Active =>
        _checks.Where(c => c.Episode is not null).Select(c => (c.Id, c.Episode!.Level));

    public void Tick(PhaseContext ctx)
    {
        Track(ctx);
        foreach (var check in _checks)
        {
            var reading = check.Read(ctx);
            if (!reading.Outside)
            {
                Close(check, ctx);
                continue;
            }

            var ep = check.Episode ??= new Episode(ctx.Now, ctx.Session.SimTime);
            ep.Worst = Math.Max(ep.Worst, reading.Value);
            if (check.Serious)
            {
                if (ep.Level == 0) Escalate(check, ep, 1, ctx);
                else if (ep.Level == 1 && check.SeriousRepeats && ctx.Now - ep.LastLevelAt >= _o.SeriousRepeat) Escalate(check, ep, 2, ctx);
                continue;
            }

            var interval = check.Grace > _o.MinLevelInterval ? check.Grace : _o.MinLevelInterval;
            if (ep.Level == 0 && ctx.Now - ep.Start >= check.Grace) Escalate(check, ep, 1, ctx);
            else if (ep.Level is 1 or 2 && ctx.Now - ep.LastLevelAt >= interval) Escalate(check, ep, ep.Level + 1, ctx);
        }
    }

    private void Escalate(CheckDef check, Episode ep, int level, PhaseContext ctx)
    {
        bool sent = check.Warn(ctx, level);
        if (!sent && level == 2 && check.Id == ConformanceCheck.MissedHandoff) level = 3; // guard not monitored (decision 7)
        ep.Level = level;
        ep.LastLevelAt = ctx.Now;
        ctx.Session.Log.Add(new ConformanceWarning(ctx.Session.SimTime, check.Id, level, check.Serious));
    }

    private void Close(CheckDef check, PhaseContext ctx)
    {
        if (check.Episode is not { } ep) return;
        if (ep.Level >= 1 || check.Serious)
            ctx.Session.Log.Add(new ConformanceEpisode(ctx.Session.SimTime, check.Id, ep.SimStart, ep.Level, check.Serious, Math.Round(ep.Worst, 1), check.Unit));
        check.Episode = null;
    }

    /// <summary>Notes clearance changes (start of suppression and compliance timers) and one-off events.</summary>
    private void Track(PhaseContext ctx)
    {
        var s = ctx.State;
        var c = ctx.Clearance;

        if (!s.OnGround && !_airborne) _airborneSince = ctx.Now;
        _airborne = !s.OnGround;

        if (c.ClearedAltitude != _cleared)
        {
            _cleared = c.ClearedAltitude;
            _clearedAt = ctx.Now;
            _altitudeAtClearance = s.IndicatedAltitudeFt;
            double delta = (_cleared?.NominalFeet ?? s.IndicatedAltitudeFt) - s.IndicatedAltitudeFt;
            _verticalDirection = Math.Abs(delta) <= _o.AltitudeBustFt ? 0 : Math.Sign(delta);
            _complying = false;
        }

        if (c.SpeedRestrictionKt != _assignedSpeed)
        {
            _assignedSpeed = c.SpeedRestrictionKt;
            _speedAssignedAt = ctx.Now;
        }

        if (c.AssignedHeadingDeg != _assignedHeading)
        {
            _assignedHeading = c.AssignedHeadingDeg;
            _headingAssignedAt = ctx.Now;
            _headingArmed = false;
        }

        if (c.DirectTo != _directTo)
        {
            _directTo = c.DirectTo;
            _directFrom = Position(s);
        }

        foreach (var e in ctx.NewEvents)
        {
            switch (e)
            {
                case PassedHoldShort p when !c.IsCrossCleared(p.Runway) && !c.IsLineUpCleared(p.Runway) && !c.IsTakeoffCleared(p.Runway):
                    _incursion = _incursionNew = true;
                    break;
                case TakeoffRoll t when !c.IsTakeoffCleared(t.Runway):
                    _unclearedTakeoff = _unclearedTakeoffNew = true;
                    break;
            }
        }
    }

    // ---- Altitude bust: more than 300 ft past the cleared altitude in the direction of travel.

    private Reading ReadAltitudeBust(PhaseContext ctx)
    {
        if (!_airborne || _cleared is not { } cleared || ctx.Now - _clearedAt < _o.AltitudeSuppression) return Reading.Inside;
        if (ctx.Clearance.Approach is not null) return Reading.Inside; // cleared for an approach: the procedure sets the descent
        double past = ctx.State.IndicatedAltitudeFt - cleared.NominalFeet;
        double excess = _verticalDirection switch
        {
            > 0 => past,
            < 0 => -past,
            _ => Math.Abs(past), // level: either side
        };
        return excess > _o.AltitudeBustFt ? Reading.Out(excess) : Reading.Inside;
    }

    private bool WarnAltitudeBust(PhaseContext ctx, int level)
    {
        var cleared = _cleared!.Value;
        bool above = ctx.State.IndicatedAltitudeFt > cleared.NominalFeet;
        return level switch
        {
            1 => Say(ctx, [new CheckAltitude(cleared)]),
            2 => Instruct(ctx, [above ? new DescendTo(cleared, Immediately: true) : new ClimbTo(cleared, Immediately: true)], AltitudeReadback(cleared)),
            _ => true,
        };
    }

    // ---- Altitude not complying: no climb or descent started 60 s after the instruction.

    private Reading ReadNotComplying(PhaseContext ctx)
    {
        if (!_airborne || _cleared is null || _verticalDirection == 0 || _complying || ctx.Clearance.Approach is not null) return Reading.Inside;
        double moved = _verticalDirection * (ctx.State.IndicatedAltitudeFt - _altitudeAtClearance);
        if (moved >= _o.ComplyingMovedFt || _verticalDirection * ctx.State.VerticalSpeedFpm >= _o.ComplyingVerticalSpeedFpm)
        {
            _complying = true;
            return Reading.Inside;
        }
        var since = _clearedAt > _airborneSince ? _clearedAt : _airborneSince;
        return Reading.Out((ctx.Now - since).TotalSeconds);
    }

    private bool WarnNotComplying(PhaseContext ctx, int level)
    {
        var cleared = _cleared!.Value;
        bool climbing = _verticalDirection > 0;
        return level switch
        {
            1 => Say(ctx, [new ConfirmVertical(climbing, cleared)]),
            2 => Instruct(ctx, [climbing ? new ClimbTo(cleared) : new DescendTo(cleared)], AltitudeReadback(cleared)),
            _ => true,
        };
    }

    // ---- Heading: more than 20 degrees off an assigned heading.

    private Reading ReadHeading(PhaseContext ctx)
    {
        if (!_airborne || _assignedHeading is not { } hdg) return Reading.Inside;
        double off = Math.Abs(GeoMath.AngleDiffDeg(ctx.State.HeadingMagneticDeg, hdg));
        if (!_headingArmed)
        {
            if (off > _o.HeadingToleranceDeg && ctx.Now - _headingAssignedAt < _o.HeadingArmAfter) return Reading.Inside; // still turning
            _headingArmed = true;
        }
        return off > _o.HeadingToleranceDeg ? Reading.Out(off) : Reading.Inside;
    }

    private bool WarnHeading(PhaseContext ctx, int level)
    {
        int hdg = _assignedHeading!.Value;
        return level switch
        {
            1 => Say(ctx, [new CheckHeading(), new TurnHeading(TurnDirection.None, hdg)]),
            2 => Instruct(ctx, [new TurnHeading(TurnTowards(ctx.State, hdg), hdg, Immediately: true)], HeadingReadback(hdg)),
            _ => true,
        };
    }

    // ---- Off route: more than 3 nm off the cleared route with no heading assigned (En route and Descent only).

    private Reading ReadOffRoute(PhaseContext ctx)
    {
        if (!_airborne || ctx.Clearance.AssignedHeadingDeg is not null) return Reading.Inside;
        if (ctx.Session.Phase is not (FlightPhase.EnRoute or FlightPhase.Descent)) return Reading.Inside;
        if (NearestLeg(ctx) is not { } leg) return Reading.Inside;
        return Math.Abs(leg.CrossTrackNm) > _o.OffRouteNm ? Reading.Out(Math.Abs(leg.CrossTrackNm)) : Reading.Inside;
    }

    private bool WarnOffRoute(PhaseContext ctx, int level)
    {
        if (level == 1) return Say(ctx, [new SayIntentions()]);
        if (level != 2 || NearestLeg(ctx) is not { } leg) return true;

        // Vector back: the leg's course, turned 30 degrees towards the route, in degrees magnetic, rounded to 5.
        double interceptTrue = leg.CourseTrueDeg - Math.Sign(leg.CrossTrackNm) * _o.InterceptDeg;
        int hdg = RoundHeading(interceptTrue - Variation(ctx.State));
        return Instruct(ctx, [new TurnHeading(TurnTowards(ctx.State, hdg), hdg)], HeadingReadback(hdg),
            onSent: () => ctx.Clearance.AssignedHeadingDeg = hdg);
    }

    private (double CrossTrackNm, double CourseTrueDeg)? NearestLeg(PhaseContext ctx)
    {
        var points = RoutePoints(ctx);
        if (points.Count < 2) return null;
        var pos = Position(ctx.State);
        (double, double)? best = null;
        for (int i = 0; i + 1 < points.Count; i++)
        {
            double xt = CrossTrackNm(points[i], points[i + 1], pos);
            if (best is null || Math.Abs(xt) < Math.Abs(best.Value.Item1)) best = (xt, GeoMath.BearingDeg(points[i], points[i + 1]));
        }
        return best;
    }

    /// <summary>
    /// The cleared route from origin to destination (the airspace route line), or after a direct-to: from where
    /// the direct was given, through the rest of the plan, to the destination.
    /// </summary>
    private List<GeoPoint> RoutePoints(PhaseContext ctx)
    {
        if (_directTo is not { } fix) return ctx.Airspace.Route.Points.ToList();
        var route = ctx.Plan.Route;
        int i = route.ToList().FindIndex(w => string.Equals(w.Ident, fix, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return [];
        return [_directFrom, .. route.Skip(i).Select(w => w.Position), ctx.Airspace.Route.Destination];
    }

    // ---- Speed: above 250 kt IAS below FL100, or more than 15 kt above an assigned speed.

    private Reading ReadSpeed(PhaseContext ctx) =>
        _airborne && SpeedTarget(ctx) is { } target ? Reading.Out(ctx.State.IndicatedAirspeedKt - target) : Reading.Inside;

    private int? SpeedTarget(PhaseContext ctx)
    {
        var s = ctx.State;
        if (s.IndicatedAltitudeFt < _o.SpeedLimitBelowFt && s.IndicatedAirspeedKt > _o.SpeedLimitKt) return (int)_o.SpeedLimitKt;
        if (ctx.Clearance.SpeedRestrictionKt is { } assigned && ctx.Now - _speedAssignedAt >= _o.AssignedSpeedSuppression
            && s.IndicatedAirspeedKt > assigned + _o.AssignedSpeedToleranceKt) return assigned;
        return null;
    }

    private bool WarnSpeed(PhaseContext ctx, int level) =>
        level >= 3 || SpeedTarget(ctx) is not { } target || Say(ctx, [new Speed(target, SpeedAction.Reduce)]);

    // ---- Squawk: wrong code, or mode not ALT, once airborne.

    private Reading ReadSquawk(PhaseContext ctx)
    {
        if (!_airborne || ctx.Clearance.Squawk is not { } code) return Reading.Inside;
        return ctx.State.TransponderCode != code || ctx.State.TransponderMode != TransponderMode.Alt ? Reading.Out(0) : Reading.Inside;
    }

    private bool WarnSquawk(PhaseContext ctx, int level)
    {
        int code = ctx.Clearance.Squawk!.Value;
        return level switch
        {
            1 => Say(ctx, [new Squawk(code)]),
            2 => Say(ctx, [new Squawk(code, Recycle: true)]),
            _ => true,
        };
    }

    // ---- Missed handoff: not on the new frequency 45 s after the handoff.

    private Reading ReadMissedHandoff(PhaseContext ctx)
    {
        if (ctx.Clearance.HandoffTo is not { } to || ctx.State.IsTunedTo(to)) return Reading.Inside;
        return Reading.Out(0);
    }

    private bool WarnMissedHandoff(PhaseContext ctx, int level)
    {
        var c = ctx.Clearance;
        if (c.HandoffFrom is not { } caller || c.HandoffTo is not { } to) return true;
        switch (level)
        {
            case 1:
                ctx.Conversation.Say(caller, [new Contact(to)], TransmissionPriority.Instruction, ctx.Now);
                return true;
            case 2:
                if (!ctx.State.IsTunedTo(_o.GuardKhz)) return false;
                ctx.Conversation.Say(caller with { FrequencyKhz = _o.GuardKhz }, [new Contact(to)], TransmissionPriority.Instruction, ctx.Now);
                return true;
            default:
                return true; // level 3: lost-comms note in the log
        }
    }

    // ---- Runway incursion: passed a hold-short line without a cross or line-up clearance. Serious.

    private Reading ReadIncursion(PhaseContext ctx)
    {
        if (_incursionNew)
        {
            _incursionNew = false;
            return Reading.Out(0);
        }
        if (_incursion && ctx.State.GroundSpeedKt >= 1) return Reading.Out(0);
        _incursion = false;
        return Reading.Inside;
    }

    private bool WarnIncursion(PhaseContext ctx, int level) => level != 1 || Urgent(ctx, [new StopImmediately()]);

    // ---- Takeoff without clearance. Serious.

    private Reading ReadUnclearedTakeoff(PhaseContext ctx)
    {
        if (_unclearedTakeoffNew)
        {
            _unclearedTakeoffNew = false;
            return Reading.Out(ctx.State.GroundSpeedKt);
        }
        if (_unclearedTakeoff && ctx.State.OnGround && ctx.State.GroundSpeedKt >= 30) return Reading.Out(ctx.State.GroundSpeedKt);
        _unclearedTakeoff = false;
        return Reading.Inside;
    }

    private bool WarnUnclearedTakeoff(PhaseContext ctx, int level) => level != 1 || Urgent(ctx, [new StopImmediately(Emphasis: true)]);

    // ---- Landing without clearance: below 200 ft AGL on final without a landing clearance. Serious.

    private Reading ReadUnclearedLanding(PhaseContext ctx)
    {
        if (!_airborne || ctx.State.AltitudeAglFt >= _o.FinalMaxAglFt) return Reading.Inside;
        if (ctx.Session.Phase is <= FlightPhase.InitialClimb) return Reading.Inside; // departure climb-out is not a final
        if (ctx.Planning.MissedApproachInProgress) return Reading.Inside;          // going around, not landing
        foreach (var runway in ctx.Destination.Runways)
        {
            foreach (var end in new[] { runway.First, runway.Second })
            {
                if (OnFinal(ctx.State, runway, end) && !ctx.Clearance.IsLandingCleared(runway))
                    return Reading.Out(_o.FinalMaxAglFt - ctx.State.AltitudeAglFt);
            }
        }
        return Reading.Inside;
    }

    /// <summary>Decision 6: within 2 nm of the threshold and 10 degrees of the extended centreline, or over the runway, flying the landing direction.</summary>
    private bool OnFinal(SimState s, Runway runway, RunwayEnd end)
    {
        var pos = Position(s);
        if (Math.Abs(GeoMath.AngleDiffDeg(s.HeadingTrueDeg, end.HeadingTrueDeg)) > 30) return false;

        double nm = GeoMath.DistanceNm(end.Position, pos);
        double offset = Math.Abs(GeoMath.AngleDiffDeg(GeoMath.BearingDeg(end.Position, pos), end.HeadingTrueDeg + 180));
        if (nm <= _o.FinalMaxDistanceNm && offset <= _o.FinalMaxOffsetDeg) return true;

        var (along, cross) = runway.Project(pos);
        return along >= 0 && along <= runway.LengthM && Math.Abs(cross) <= runway.WidthM / 2;
    }

    private bool WarnUnclearedLanding(PhaseContext ctx, int level) => level > 2 || Urgent(ctx, [new GoAround(Repeated: level == 2)]);

    // ---- Taxi off route: more than 50 m from the cleared taxi route (only when a route is known).

    private Reading ReadTaxiOffRoute(PhaseContext ctx)
    {
        if (!ctx.State.OnGround || ctx.Session.Phase is not (FlightPhase.TaxiOut or FlightPhase.TaxiIn)) return Reading.Inside;
        if (ctx.Clearance.Taxi is not { Path.Count: >= 2 } taxi) return Reading.Inside;
        double metres = DistanceToPathM(taxi.Path, Position(ctx.State));
        return metres > _o.TaxiOffRouteM ? Reading.Out(metres) : Reading.Inside;
    }

    private bool WarnTaxiOffRoute(PhaseContext ctx, int level)
    {
        if (level == 1) return Say(ctx, [new HoldPosition()]);
        if (level != 2 || ctx.Session.Phase != FlightPhase.TaxiOut || ctx.DepartureRunway is not { } runway) return true;

        // Re-route from where the aircraft is now.
        var plan = ctx.Options.TaxiPlanner.PlanToHoldingPoint(ctx.Origin, Position(ctx.State), runway);
        string rwy = runway.Designator;
        string? hp = plan.HoldingPoint?.Name;
        var b = ReadbackExpectation.Builder();
        if (hp is not null) b.Add(ReadbackItem.HoldingPoint, ReadbackValues.HoldingPoint(hp), new TaxiHoldingPoint(hp));
        b.Add(ReadbackItem.Runway, ReadbackValues.Runway(rwy), new RunwayIs(rwy));
        if (plan.HoldShorts.Count > 0)
            b.Add(ReadbackItem.HoldShorts, ReadbackValues.HoldShorts(plan.HoldShorts), plan.HoldShorts.Select(h => (AtcMessagePart)new HoldShort(h)).ToArray());
        return Instruct(ctx, [new TaxiVia(hp, rwy, plan.Via, plan.HoldShorts)], b.Build(),
            onSent: () => ctx.Clearance.Taxi = new TaxiClearance(hp ?? $"holding point runway {rwy}", rwy, plan.Via, plan.HoldShorts, plan.Path));
    }

    // ---- Transmission helpers. Normal warnings come from the unit in control; serious ones from the unit the pilot is on.

    private static ControllerUnit? ControllingUnit(PhaseContext ctx) =>
        ctx.Session.CurrentUnit ?? ctx.Airspace.UnitOn(ctx.State.TransmitFrequencyKhz);

    private static bool Say(PhaseContext ctx, IReadOnlyList<AtcMessagePart> parts)
    {
        if (ControllingUnit(ctx) is not { } unit) return false;
        ctx.Conversation.Say(unit, parts, TransmissionPriority.Instruction, ctx.Now);
        return true;
    }

    private static bool Instruct(PhaseContext ctx, IReadOnlyList<AtcMessagePart> parts, ReadbackExpectation readback, Action? onSent = null)
    {
        if (ControllingUnit(ctx) is not { } unit) return false;
        ctx.Conversation.Transmit(unit, parts, TransmissionPriority.Instruction, ctx.Now, readback, onSent, tag: "conformance");
        return true;
    }

    private static bool Urgent(PhaseContext ctx, IReadOnlyList<AtcMessagePart> parts)
    {
        if ((ctx.Airspace.UnitOn(ctx.State.TransmitFrequencyKhz) ?? ctx.Session.CurrentUnit) is not { } unit) return false;
        ctx.Conversation.Say(unit, parts, TransmissionPriority.Urgent, ctx.Now);
        return true;
    }

    private static ReadbackExpectation AltitudeReadback(AltitudeTarget alt) =>
        ReadbackExpectation.Builder().Add(ReadbackItem.Altitude, ReadbackValues.Altitude(alt), new ClimbTo(alt)).Build();

    private static ReadbackExpectation HeadingReadback(int hdg) =>
        ReadbackExpectation.Builder().Add(ReadbackItem.Heading, ReadbackValues.Heading(hdg), new TurnHeading(TurnDirection.None, hdg)).Build();

    // ---- Geometry.

    private static GeoPoint Position(SimState s) => new(s.Latitude, s.Longitude);

    private static double Variation(SimState s) => GeoMath.AngleDiffDeg(s.HeadingTrueDeg, s.HeadingMagneticDeg);

    private static TurnDirection TurnTowards(SimState s, int headingMag) =>
        GeoMath.AngleDiffDeg(headingMag, s.HeadingMagneticDeg) >= 0 ? TurnDirection.Right : TurnDirection.Left;

    private static int RoundHeading(double deg)
    {
        int h = (int)(Math.Round(GeoMath.NormalizeDeg(deg) / 5) * 5);
        return h is 0 or 360 ? 360 : h;
    }

    /// <summary>Signed cross-track distance (positive right of the leg), measured to the nearest point of the segment.</summary>
    private static double CrossTrackNm(GeoPoint a, GeoPoint b, GeoPoint p)
    {
        var (xt, along) = GeoMath.TrackOffsets(a, b, p);
        double sign = xt == 0 ? 1 : Math.Sign(xt);
        if (along < 0) return sign * GeoMath.DistanceNm(a, p);
        if (along > GeoMath.DistanceNm(a, b)) return sign * GeoMath.DistanceNm(b, p);
        return xt;
    }

    private static double DistanceToPathM(IReadOnlyList<GeoPoint> path, GeoPoint p)
    {
        var frame = new LocalFrame(p);
        double best = double.MaxValue;
        for (int i = 0; i + 1 < path.Count; i++)
        {
            var (ax, ay) = frame.ToLocal(path[i]);
            var (bx, by) = frame.ToLocal(path[i + 1]);
            double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
            double t = len2 == 0 ? 0 : Math.Clamp(-(ax * dx + ay * dy) / len2, 0, 1);
            double cx = ax + t * dx, cy = ay + t * dy;
            best = Math.Min(best, Math.Sqrt(cx * cx + cy * cy));
        }
        return best;
    }
}
