using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Events;
using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Messages;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Planning;
using OpenSkyAtc.Core.Radio;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Core.Phases;

/// <summary>
/// Design doc go-around detection: below 300 ft AGL near a destination runway, then climbing more than 300 ft
/// without a touchdown. "Near" is within 3 nm of a threshold and 15 degrees of its centreline, or over the runway.
/// </summary>
internal sealed class GoAroundDetector
{
    private const double LowAglFt = 300, ClimbFt = 300, NearNm = 3, NearOffsetDeg = 15;
    private double? _lowest;

    public bool Update(PhaseContext ctx)
    {
        var s = ctx.State;
        if (s.OnGround || ctx.NewEvents.OfType<Touchdown>().Any())
        {
            _lowest = null;
            return false;
        }
        if (NearRunway(ctx) && s.AltitudeAglFt < LowAglFt) _lowest = Math.Min(_lowest ?? double.MaxValue, s.AltitudeAglFt);
        if (_lowest is { } low && s.AltitudeAglFt > low + ClimbFt)
        {
            _lowest = null;
            return true;
        }
        return false;
    }

    private static bool NearRunway(PhaseContext ctx)
    {
        var pos = ctx.Position;
        foreach (var r in ctx.Destination.Runways)
        {
            var (along, cross) = r.Project(pos);
            if (along >= 0 && along <= r.LengthM && Math.Abs(cross) <= r.WidthM) return true;
            foreach (var end in new[] { r.First, r.Second })
            {
                double offset = Math.Abs(GeoMath.AngleDiffDeg(GeoMath.BearingDeg(end.Position, pos), end.HeadingTrueDeg + 180));
                if (GeoMath.DistanceNm(end.Position, pos) <= NearNm && offset <= NearOffsetDeg) return true;
            }
        }
        return false;
    }
}

/// <summary>Missed-approach instructions shared by Approach and Final (design doc: "runway heading, climb 5000 ft").</summary>
internal static class MissedApproach
{
    public const int AltitudeFt = 5000;

    public static AltitudeTarget Altitude(PhaseContext ctx) => ctx.Planning.ToTarget(AltitudeFt, ctx.Destination);

    /// <summary>"Maintain 160 knots until 4 miles final" lapses at 4 nm without a call, whichever unit has the aircraft.</summary>
    public static void LapseFinalSpeed(PhaseContext ctx)
    {
        var c = ctx.Clearance;
        if (c.Approach is null || c.SpeedRestrictionKt is null || ctx.Planning.ArrivalRunway is not { } runway) return;
        if (GeoMath.DistanceNm(ctx.Position, runway.Position) <= ctx.Planning.Options.FinalSpeedUntilNm) c.SpeedRestrictionKt = null;
    }

    /// <summary>Clears the landing clearance and sets runway heading (magnetic) and the missed-approach altitude when sent.</summary>
    public static Action OnSent(PhaseContext ctx, RunwayEnd? runway, AltitudeTarget altitude) => () =>
    {
        ctx.Clearance.LandingCleared = null;
        ctx.Clearance.Approach = null;
        ctx.Clearance.SpeedRestrictionKt = null;
        ctx.Clearance.ClearedAltitude = altitude;
        if (runway is not null)
        {
            double variation = GeoMath.AngleDiffDeg(ctx.State.HeadingTrueDeg, ctx.State.HeadingMagneticDeg);
            int hdg = (int)Math.Round(GeoMath.NormalizeDeg(runway.HeadingTrueDeg - variation));
            ctx.Clearance.AssignedHeadingDeg = hdg == 0 ? 360 : hdg;
        }
    };
}

/// <summary>
/// Phase 11: Approach. On check-in: "identified", descend to the approach altitude with QNH, and either the STAR
/// and approach together (when the sim has the STAR) or "vectoring for ILS approach". Vectors: downwind, base and a
/// final vector given with the approach clearance; speed control for jets. Established within 10 nm: hand off to
/// Tower. A go-around while with Approach gets the missed-approach instruction; vectors start again once level.
/// </summary>
public sealed class ApproachPhase : Phase
{
    private const double HandoffWithinNm = 10;

    private readonly GoAroundDetector _goAround = new();
    private bool _checkedIn;
    private Vectoring? _vectoring;
    private VectorLeg? _lastLeg;
    private int? _lastHeading;
    private Exchange? _approachClearance;
    private bool _established;
    private Exchange? _handoff;
    private bool _missedApproach;
    private EstablishedRegistration? _registration;

    public override FlightPhase Id => FlightPhase.Approach;
    public override ControllerUnit? Unit(PhaseContext ctx) => ctx.Airspace.ArrivalApproachUnit;

    /// <summary>Coming back from Final after a go-around: the missed approach (runway heading) is still being flown.</summary>
    public override void OnEnter(PhaseContext ctx) => _missedApproach = ctx.Planning.MissedApproachInProgress;

    public override PhaseResult OnPilotIntent(PhaseContext ctx, PilotIntent intent)
    {
        var tower = ctx.Airspace.ArrivalTowerUnit;
        if (intent is CheckIn && tower is not null && ctx.CalledUnit == tower)
            return PhaseResult.To(FlightPhase.Final, _handoff is null ? "called tower before the handoff" : "checked in with tower", redispatch: true);
        if (!CallsMe(ctx)) return PhaseResult.Unhandled;

        switch (intent)
        {
            case CheckIn when !_checkedIn && _missedApproach:
                // After a go-around: keep the missed approach; vectors start once level (see Tick).
                _checkedIn = true;
                ctx.Planning.ChooseArrivalRunway(ctx.State);
                ctx.Conversation.Say(Unit(ctx)!, CheckInParts(ctx, intent, firstRadarUnit: false), TransmissionPriority.Reply, ctx.Now + ctx.Conversation.CheckInDelay());
                return PhaseResult.Stay;
            case CheckIn when !_checkedIn:
                _checkedIn = true;
                if (ctx.Planning.ChooseArrivalRunway(ctx.State) is not { } runway)
                {
                    ctx.Conversation.Say(Unit(ctx)!, CheckInParts(ctx, intent, firstRadarUnit: false), TransmissionPriority.Reply, ctx.Now + ctx.Conversation.CheckInDelay());
                    return PhaseResult.Stay;
                }
                ctx.Clearance.Runway = runway.Designator;
                StartArrival(ctx, runway, CheckInParts(ctx, intent, firstRadarUnit: false), TransmissionPriority.Reply, ctx.Now + ctx.Conversation.CheckInDelay());
                return PhaseResult.Stay;
            case ReportEstablished when _approachClearance is not null:
                _established = true;
                return PhaseResult.Stay;
            case GoingAround:
                IssueMissedApproach(ctx);
                return PhaseResult.Stay;
            default:
                return PhaseResult.Unhandled;
        }
    }

    /// <summary>Descent to the approach altitude with QNH, then the STAR and approach together, or "vectoring for".</summary>
    private void StartArrival(PhaseContext ctx, RunwayEnd runway, IEnumerable<AtcMessagePart> before, TransmissionPriority priority, TimeSpan due)
    {
        string type = ApproachType(runway);
        var qnh = new Qnh(FlightPlanning.Qnh(ctx.State));
        var target = ctx.Planning.ApproachAltitude();

        if (StarFor(ctx) is { } star)
        {
            // Design doc: with STAR data, clear the STAR and the approach together; the pilot flies it.
            AtcMessagePart clearance = new ClearedApproach(type, runway.Designator, star);
            _approachClearance = ClearAltitude(ctx, Unit(ctx)!, target, priority, due, before, [qnh, clearance], "approach-star");
            ctx.Clearance.Star = star;
            ctx.Clearance.Approach = $"{type} {runway.Designator}";
            RegisterEstablished(ctx, runway);
            return;
        }

        _vectoring = new Vectoring(runway, ctx.Planning, ctx.State);
        _lastLeg = null;
        _lastHeading = null;
        // Straight in: no vectors to come, so "expect" rather than "vectoring for".
        AtcMessagePart plan = _vectoring.Leg == VectorLeg.StraightIn ? new ExpectApproach(type, runway.Designator) : new VectoringFor(type, runway.Designator);
        ClearAltitude(ctx, Unit(ctx)!, target, priority, due, before, [qnh, plan], "approach-descent");
    }

    private static string? StarFor(PhaseContext ctx) =>
        ctx.Plan.PlannedStar is { } star && ctx.Destination.Stars.Contains(star, StringComparer.OrdinalIgnoreCase) ? star : null;

    public override PhaseResult Tick(PhaseContext ctx)
    {
        if (!_checkedIn || ctx.Planning.ArrivalRunway is not { } runway) return PhaseResult.Stay;
        var unit = Unit(ctx)!;
        var c = ctx.Clearance;

        if (_goAround.Update(ctx)) IssueMissedApproach(ctx);

        // After a missed approach: once level at the missed-approach altitude, descend and vector again.
        if (_missedApproach && Math.Abs(ctx.State.IndicatedAltitudeFt - MissedApproach.Altitude(ctx).NominalFeet) <= 300 && !ctx.Conversation.IsBusy(unit))
        {
            _missedApproach = false;
            ctx.Planning.MissedApproachInProgress = false;
            StartArrival(ctx, runway, [], TransmissionPriority.Instruction, ctx.Now);
            return PhaseResult.Stay;
        }

        if (_vectoring is not null && _approachClearance is null && !_missedApproach && !ctx.Conversation.IsBusy(unit))
            Vector(ctx, unit, runway);

        if (ctx.NewEvents.OfType<Established>().Any(e => e.Registration == _registration))
        {
            _established = true;
            c.AssignedHeadingDeg = null; // localiser captured: no more heading to hold
        }

        double toThreshold = GeoMath.DistanceNm(ctx.Position, runway.Position);
        MissedApproach.LapseFinalSpeed(ctx);

        if (_established && _handoff is null && toThreshold <= HandoffWithinNm && ctx.Airspace.ArrivalTowerUnit is { } tower)
            _handoff = Handoff(ctx, unit, tower, ctx.Now, TransmissionPriority.Instruction, "contact-tower");
        return PhaseResult.Stay;
    }

    /// <summary>Sends the next vector when the leg changes or the heading moves by 10 degrees; the final one with the clearance.</summary>
    private void Vector(PhaseContext ctx, ControllerUnit unit, RunwayEnd runway)
    {
        var step = _vectoring!.Update(ctx.State);
        var c = ctx.Clearance;
        bool newLeg = step.Leg != _lastLeg;
        bool headingMoved = step.HeadingMag is { } h && (_lastHeading is not { } last || Math.Abs(GeoMath.AngleDiffDeg(h, last)) >= ctx.Planning.Options.MinHeadingChangeDeg);
        if (!step.IsFinal && !(step.HeadingMag is not null && (newLeg || headingMoved))) return;

        var parts = new List<AtcMessagePart>();
        var b = ReadbackExpectation.Builder();
        if (step.HeadingMag is { } hdg)
        {
            var turn = new TurnHeading(TurnTowards(ctx.State, hdg), hdg);
            parts.Add(turn);
            b.Add(ReadbackItem.Heading, ReadbackValues.Heading(hdg), new TurnHeading(TurnDirection.None, hdg));
            if (step.IsFinal) parts.Add(new ClosingLocaliser(step.AircraftSideRight));
        }

        // Speed: only on a new leg, and only when it is a reduction.
        int? speed = newLeg && step.SpeedKt is { } kt && ctx.State.IndicatedAirspeedKt > kt + 5 ? kt : null;
        string type = ApproachType(runway);
        if (step.IsFinal)
        {
            parts.Add(new ClearedApproach(type, runway.Designator));
            parts.Add(new ReportEstablishedRequest());
            b.Add(ReadbackItem.Runway, ReadbackValues.Runway(runway.Designator), new RunwayIs(runway.Designator))
             .Add(ReadbackItem.Clearance, ReadbackValues.Approach, new ClearedApproach(type, runway.Designator));
            speed = step.SpeedKt is { } fs && ctx.State.IndicatedAirspeedKt > fs - 5 ? fs : null;
            if (speed is { } s) parts.Add(new SpeedUntil(s, ctx.Planning.Options.FinalSpeedUntilNm));
        }
        else if (speed is { } s)
        {
            parts.Add(new Speed(s, SpeedAction.Reduce));
        }
        if (speed is { } sp) b.Add(ReadbackItem.Speed, ReadbackValues.Speed(sp), new Speed(sp, SpeedAction.Reduce));

        int? heading = step.HeadingMag;
        var exchange = ctx.Conversation.Transmit(unit, parts, TransmissionPriority.Instruction, ctx.Now, b.Build(), onSent: () =>
        {
            c.AssignedHeadingDeg = heading ?? (step.IsFinal ? null : c.AssignedHeadingDeg);
            if (speed is { } kt2) c.SpeedRestrictionKt = kt2;
            if (step.IsFinal)
            {
                c.Approach = $"{type} {runway.Designator}";
                c.DirectTo = null;
            }
        }, tag: step.IsFinal ? "approach" : "vector");

        _lastLeg = step.Leg;
        _lastHeading = heading ?? _lastHeading;
        if (step.IsFinal)
        {
            _approachClearance = exchange;
            RegisterEstablished(ctx, runway);
        }
    }

    private void RegisterEstablished(PhaseContext ctx, RunwayEnd runway)
    {
        if (_registration is not null) ctx.Events.Unregister(_registration);
        _registration = ctx.Events.RegisterEstablished(this, runway, maxDistanceNm: 25, tag: "established");
    }

    private static TurnDirection TurnTowards(SimState s, int headingMag) =>
        GeoMath.AngleDiffDeg(headingMag, s.HeadingMagneticDeg) >= 0 ? TurnDirection.Right : TurnDirection.Left;

    private void IssueMissedApproach(PhaseContext ctx)
    {
        if (_missedApproach || Unit(ctx) is not { } unit) return;
        _missedApproach = true;
        _handoff = null;
        _approachClearance = null;
        _vectoring = null;
        _established = false;
        ctx.Planning.MissedApproachInProgress = true;
        var altitude = MissedApproach.Altitude(ctx);
        var readback = ReadbackExpectation.Builder().Add(ReadbackItem.Altitude, ReadbackValues.Altitude(altitude), new ClimbTo(altitude)).Build();
        ctx.Conversation.Transmit(unit, [new FlyRunwayHeading(), new ClimbTo(altitude)], TransmissionPriority.Urgent, ctx.Now, readback,
            onSent: MissedApproach.OnSent(ctx, ctx.Planning.ArrivalRunway, altitude), tag: "missed-approach");
        ctx.Session.Log.Add(new GoAroundLogged(ctx.Session.SimTime, ctx.Session.Phase));
    }

    public override IEnumerable<PilotIntent> AvailableIntents(PhaseContext ctx)
    {
        var list = new List<PilotIntent>();
        if (!_checkedIn) list.Add(new CheckIn((int)(Math.Round(ctx.State.IndicatedAltitudeFt / 100) * 100), ctx.Clearance.ClearedAltitude));
        if (_approachClearance is { State: not ExchangeState.Queued } && !_established) list.Add(new ReportEstablished());
        if (_handoff is { State: not ExchangeState.Queued }) list.Add(new CheckIn((int)(Math.Round(ctx.State.IndicatedAltitudeFt / 100) * 100), ctx.Clearance.ClearedAltitude));
        if (_checkedIn) list.Add(new GoingAround());
        return list;
    }

    internal static string ApproachType(RunwayEnd runway) => runway.ApproachType ?? "ILS";
}

/// <summary>Phase 12: Final with Tower. Landing clearance with wind; touchdown ends it; a go-around goes back to Approach.</summary>
public sealed class FinalPhase : Phase
{
    private readonly GoAroundDetector _goAround = new();
    private Exchange? _landing;

    public override FlightPhase Id => FlightPhase.Final;
    public override ControllerUnit? Unit(PhaseContext ctx) => ctx.Airspace.ArrivalTowerUnit;

    public override PhaseResult OnPilotIntent(PhaseContext ctx, PilotIntent intent)
    {
        if (!CallsMe(ctx)) return PhaseResult.Unhandled;
        switch (intent)
        {
            case CheckIn when _landing is null:
                if (ctx.Planning.ChooseArrivalRunway(ctx.State) is not { } runway) return PhaseResult.Stay;
                string rwy = runway.Designator;
                var readback = ReadbackExpectation.Builder()
                    .Add(ReadbackItem.Runway, ReadbackValues.Runway(rwy), new RunwayIs(rwy))
                    .Add(ReadbackItem.Clearance, ReadbackValues.Land, new ClearedLand(rwy))
                    .Build();
                _landing = ctx.Conversation.Transmit(Unit(ctx)!, [WindReport.From(ctx.State), new ClearedLand(rwy)], TransmissionPriority.Reply,
                    ctx.Now + ctx.Conversation.CheckInDelay(), readback,
                    onSent: () =>
                    {
                        ctx.Clearance.Runway = rwy;
                        ctx.Clearance.LandingCleared = rwy;
                    }, tag: "landing");
                return PhaseResult.Stay;
            case GoingAround:
                return GoAround(ctx, "pilot reported going around");
            default:
                return PhaseResult.Unhandled;
        }
    }

    public override PhaseResult Tick(PhaseContext ctx)
    {
        MissedApproach.LapseFinalSpeed(ctx);
        if (ctx.NewEvents.OfType<Touchdown>().Any()) return PhaseResult.To(FlightPhase.LandingRoll, "touchdown");
        if (_goAround.Update(ctx)) return GoAround(ctx, "go-around detected");
        return PhaseResult.Stay;
    }

    /// <summary>"Fly runway heading, climb 5000 ft, contact Approach", then back to the Approach phase (design doc go-around branch).</summary>
    private PhaseResult GoAround(PhaseContext ctx, string reason)
    {
        var tower = Unit(ctx)!;
        var altitude = MissedApproach.Altitude(ctx);
        var approach = ctx.Airspace.ArrivalApproachUnit;
        var parts = new List<AtcMessagePart> { new FlyRunwayHeading(), new ClimbTo(altitude) };
        var b = ReadbackExpectation.Builder().Add(ReadbackItem.Altitude, ReadbackValues.Altitude(altitude), new ClimbTo(altitude));
        if (approach is not null)
        {
            parts.Add(new Contact(approach));
            b.Add(ReadbackItem.Frequency, ReadbackValues.Frequency(approach.FrequencyKhz), new Contact(approach));
        }
        var onSent = MissedApproach.OnSent(ctx, ctx.Planning.ArrivalRunway, altitude);
        ctx.Planning.MissedApproachInProgress = true;
        ctx.Conversation.Transmit(tower, parts, TransmissionPriority.Urgent, ctx.Now, b.Build(), onSent: () =>
        {
            onSent();
            if (approach is not null) ctx.Clearance.SetHandoff(tower, approach);
        }, tag: "missed-approach");
        ctx.Session.Log.Add(new GoAroundLogged(ctx.Session.SimTime, FlightPhase.Final));
        return approach is null ? PhaseResult.Stay : PhaseResult.To(FlightPhase.Approach, reason);
    }

    public override IEnumerable<PilotIntent> AvailableIntents(PhaseContext ctx) =>
        _landing is null
            ? [new CheckIn((int)(Math.Round(ctx.State.IndicatedAltitudeFt / 100) * 100), ctx.Clearance.ClearedAltitude), new GoingAround()]
            : [new GoingAround()];
}

/// <summary>Phase 13: landing roll. "Vacate when able, contact Ground"; ends off the runway below 30 kt.</summary>
public sealed class LandingRollPhase : Phase
{
    public override FlightPhase Id => FlightPhase.LandingRoll;
    public override ControllerUnit? Unit(PhaseContext ctx) => ctx.Airspace.ArrivalTowerUnit;

    public override void OnEnter(PhaseContext ctx)
    {
        ctx.Clearance.LandingCleared = null;
        if (Unit(ctx) is not { } tower) return;
        var ground = ctx.Airspace.ArrivalGroundUnit;
        if (ground is not null && ground != tower)
        {
            var contact = new Contact(ground);
            var readback = ReadbackExpectation.Builder().Add(ReadbackItem.Frequency, ReadbackValues.Frequency(ground.FrequencyKhz), contact).Build();
            ctx.Conversation.Transmit(tower, [new VacateWhenAble(), contact], TransmissionPriority.Instruction,
                ctx.Now + ctx.Conversation.CorrectionDelay(), readback, onSent: () => ctx.Clearance.SetHandoff(tower, ground), tag: "contact-ground");
        }
        else
        {
            ctx.Conversation.Say(tower, [new VacateWhenAble()], TransmissionPriority.Instruction, ctx.Now + ctx.Conversation.CorrectionDelay());
        }
    }

    public override PhaseResult Tick(PhaseContext ctx) =>
        ctx.State.OnGround && ctx.Events.CurrentRunway is null && ctx.State.GroundSpeedKt < 30
            ? PhaseResult.To(FlightPhase.TaxiIn, "vacated the runway")
            : PhaseResult.Stay;

    public override PhaseResult OnPilotIntent(PhaseContext ctx, PilotIntent intent)
    {
        if (intent is CheckIn or RequestTaxi or ReportVacated && ctx.CalledUnit is { } u && u == ctx.Airspace.ArrivalGroundUnit && u != Unit(ctx))
            return PhaseResult.To(FlightPhase.TaxiIn, "called ground", redispatch: true);
        return intent is ReportVacated && CallsMe(ctx) ? PhaseResult.Stay : PhaseResult.Unhandled;
    }

    public override IEnumerable<PilotIntent> AvailableIntents(PhaseContext ctx) => [new ReportVacated()];
}

/// <summary>Phase 14: taxi in. "Taxi to stand 27" (or "taxi to parking"); ends stopped with the parking brake set.</summary>
/// <remarks>Without parking data (step 8/9) "near the stand" cannot be checked, so stopped + brake off the runway counts.</remarks>
public sealed class TaxiInPhase : Phase
{
    private Exchange? _taxi;
    private Stand? _stand;
    private readonly CrossingClearances _crossings = new();

    public override FlightPhase Id => FlightPhase.TaxiIn;
    public override ControllerUnit? Unit(PhaseContext ctx) => ctx.Airspace.ArrivalGroundUnit;

    public override PhaseResult OnPilotIntent(PhaseContext ctx, PilotIntent intent)
    {
        if (intent is not (CheckIn or RequestTaxi or ReportVacated) || !CallsMe(ctx)) return PhaseResult.Unhandled;
        if (_taxi is not null) return PhaseResult.Stay;

        // Design doc: the plan's stand if the airport has it, otherwise the nearest suitable one; routed like taxi-out.
        var planner = ctx.Options.TaxiPlanner;
        _stand = planner.ChooseStand(ctx.Destination, ctx.Plan.DestinationStand, ctx.Position, ctx.State.EngineType);
        string? stand = _stand?.Name ?? (ctx.Destination.Taxi is null ? ctx.Plan.DestinationStand : null);
        var route = planner.PlanToStand(ctx.Destination, ctx.Position, _stand);
        if (route.FallbackReason is { } why) ctx.Session.Log.Add(new TaxiFallback(ctx.Session.SimTime, ctx.Destination.Icao, stand ?? "parking", why));

        var b = ReadbackExpectation.Builder();
        if (stand is not null) b.Add(ReadbackItem.Stand, ReadbackValues.Normalize(stand), new TaxiToStand(stand));
        if (route.HoldShorts.Count > 0)
            b.Add(ReadbackItem.HoldShorts, ReadbackValues.HoldShorts(route.HoldShorts), route.HoldShorts.Select(h => (AtcMessagePart)new HoldShort(h)).ToArray());
        var expectation = b.Build();
        bool needsReadback = expectation.Items.Count > 0;
        var taxi = new TaxiClearance(stand is null ? "parking" : $"stand {stand}", null, route.Via, route.HoldShorts, route.Path)
        {
            Crossings = route.Crossings.Select(c => (c.Runway, c.HoldShort)).ToList(),
        };
        _taxi = ctx.Conversation.Transmit(Unit(ctx)!, [new TaxiToStand(stand, route.Via, route.HoldShorts)], TransmissionPriority.Reply,
            ctx.Now + ctx.Conversation.ReplyDelay(), needsReadback ? expectation : null,
            onSent: () =>
            {
                ctx.Clearance.Taxi = taxi;
                if (!needsReadback) ctx.Clearance.ConfirmTaxi(taxi); // nothing to read back: confirmed once said
            },
            tag: "taxi-in", onReadBack: () => ctx.Clearance.ConfirmTaxi(taxi));
        return PhaseResult.Stay;
    }

    public override PhaseResult Tick(PhaseContext ctx)
    {
        if (Unit(ctx) is { } ground) _crossings.Tick(ctx, ground);
        var s = ctx.State;
        bool sent = _taxi is { State: not ExchangeState.Queued };
        // With parking data, "parked" also means at the stand (within its radius plus 30 m).
        bool atStand = _stand is null || GeoMath.DistanceM(ctx.Position, _stand.Position) <= _stand.RadiusM + 30;
        return sent && atStand && s.OnGround && s.GroundSpeedKt < 0.5 && s.ParkingBrakeSet && ctx.Events.CurrentRunway is null
            ? PhaseResult.To(FlightPhase.Parked, "parked")
            : PhaseResult.Stay;
    }

    public override IEnumerable<PilotIntent> AvailableIntents(PhaseContext ctx) =>
        _taxi is null ? [new CheckIn(), new RequestTaxi()] : [];
}

/// <summary>Phase 15: parked. Closing call; the session ends when the engines are off.</summary>
public sealed class ParkedPhase : Phase
{
    public override FlightPhase Id => FlightPhase.Parked;
    public override ControllerUnit? Unit(PhaseContext ctx) => ctx.Airspace.ArrivalGroundUnit;

    public override void OnEnter(PhaseContext ctx)
    {
        if (Unit(ctx) is { } unit)
            ctx.Conversation.Say(unit, [new ClosingCall()], TransmissionPriority.Info, ctx.Now + ctx.Conversation.CorrectionDelay());
    }

    public override PhaseResult Tick(PhaseContext ctx)
    {
        if (!ctx.Session.Ended && !ctx.State.AnyEngineRunning)
        {
            ctx.Session.Ended = true;
            ctx.Session.Log.Add(new SessionEnded(ctx.Session.SimTime, "engines off at the stand"));
        }
        return PhaseResult.Stay;
    }
}
