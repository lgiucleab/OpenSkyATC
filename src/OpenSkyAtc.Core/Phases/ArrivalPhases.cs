using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Events;
using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Messages;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Planning;
using OpenSkyAtc.Core.Radio;

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

    /// <summary>Clears the landing clearance and sets runway heading (magnetic) and the missed-approach altitude when sent.</summary>
    public static Action OnSent(PhaseContext ctx, RunwayEnd? runway, AltitudeTarget altitude) => () =>
    {
        ctx.Clearance.LandingCleared = null;
        ctx.Clearance.Approach = null;
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
/// Phase 11: Approach. On check-in: "identified", descend to the approach altitude with QNH, expect the approach.
/// Within 20 nm of the threshold: cleared for the approach, report established. Established within 10 nm:
/// hand off to Tower. A go-around while with Approach gets the missed-approach instruction and a new approach.
/// </summary>
public sealed class ApproachPhase : Phase
{
    private const double ClearApproachWithinNm = 20, HandoffWithinNm = 10;

    private readonly GoAroundDetector _goAround = new();
    private bool _checkedIn;
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
                // After a go-around: keep the missed approach; the new approach comes once level (see Tick).
                _checkedIn = true;
                ctx.Planning.ChooseArrivalRunway(ctx.State);
                ctx.Conversation.Say(Unit(ctx)!, CheckInParts(ctx, intent, firstRadarUnit: false), TransmissionPriority.Reply, ctx.Now + ctx.Conversation.CheckInDelay());
                return PhaseResult.Stay;
            case CheckIn when !_checkedIn:
                _checkedIn = true;
                var runway = ctx.Planning.ChooseArrivalRunway(ctx.State);
                var parts = CheckInParts(ctx, intent, firstRadarUnit: false);
                var target = ctx.Planning.ApproachAltitude();
                AtcMessagePart[] after = runway is null
                    ? [new Qnh(FlightPlanning.Qnh(ctx.State))]
                    : [new Qnh(FlightPlanning.Qnh(ctx.State)), new ExpectApproach(ApproachType(runway), runway.Designator)];
                ClearAltitude(ctx, Unit(ctx)!, target, TransmissionPriority.Reply, ctx.Now + ctx.Conversation.CheckInDelay(), parts, after, "approach-descent");
                if (runway is not null) ctx.Clearance.Runway = runway.Designator;
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

    public override PhaseResult Tick(PhaseContext ctx)
    {
        if (!_checkedIn || ctx.Planning.ArrivalRunway is not { } runway) return PhaseResult.Stay;
        var unit = Unit(ctx)!;

        if (_goAround.Update(ctx)) IssueMissedApproach(ctx);

        double toThreshold = GeoMath.DistanceNm(ctx.Position, runway.Position);

        // After a missed approach: once level at the missed-approach altitude, one transmission with the descent to
        // the approach altitude and the new approach clearance (own navigation until the step 7 vectors).
        bool missedComplete = _missedApproach && Math.Abs(ctx.State.IndicatedAltitudeFt - MissedApproach.Altitude(ctx).NominalFeet) <= 300;
        bool firstClearance = !_missedApproach && toThreshold <= ClearApproachWithinNm;
        if (_approachClearance is null && (missedComplete || firstClearance) && !ctx.Conversation.IsBusy(unit))
        {
            string type = ApproachType(runway);
            var parts = new List<AtcMessagePart>();
            var b = ReadbackExpectation.Builder();
            AltitudeTarget? descent = null;
            if (missedComplete)
            {
                descent = ctx.Planning.ApproachAltitude();
                var qnh = new Qnh(FlightPlanning.Qnh(ctx.State));
                parts.Add(new DescendTo(descent.Value));
                parts.Add(qnh);
                b.Add(ReadbackItem.Altitude, ReadbackValues.Altitude(descent.Value), new DescendTo(descent.Value))
                 .Add(ReadbackItem.Qnh, qnh.Hpa.ToString(System.Globalization.CultureInfo.InvariantCulture), qnh);
                _missedApproach = false;
                _established = false;
                ctx.Planning.MissedApproachInProgress = false;
            }
            parts.Add(new ClearedApproach(type, runway.Designator));
            parts.Add(new ReportEstablishedRequest());
            b.Add(ReadbackItem.Runway, ReadbackValues.Runway(runway.Designator), new RunwayIs(runway.Designator))
             .Add(ReadbackItem.Clearance, ReadbackValues.Approach, new ClearedApproach(type, runway.Designator));
            _approachClearance = ctx.Conversation.Transmit(unit, parts, TransmissionPriority.Instruction, ctx.Now, b.Build(),
                onSent: () =>
                {
                    if (descent is { } d) ctx.Clearance.ClearedAltitude = d;
                    ctx.Clearance.Approach = $"{type} {runway.Designator}";
                    ctx.Clearance.AssignedHeadingDeg = null;
                },
                tag: "approach");
            if (_registration is not null) ctx.Events.Unregister(_registration);
            _registration = ctx.Events.RegisterEstablished(this, runway, maxDistanceNm: 25, tag: "established");
        }

        if (ctx.NewEvents.OfType<Established>().Any(e => e.Registration == _registration)) _established = true;

        if (_established && _handoff is null && toThreshold <= HandoffWithinNm && ctx.Airspace.ArrivalTowerUnit is { } tower)
            _handoff = Handoff(ctx, unit, tower, ctx.Now, TransmissionPriority.Instruction, "contact-tower");
        return PhaseResult.Stay;
    }

    private void IssueMissedApproach(PhaseContext ctx)
    {
        if (_missedApproach || Unit(ctx) is not { } unit) return;
        _missedApproach = true;
        _handoff = null;
        _approachClearance = null;
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

    public override FlightPhase Id => FlightPhase.TaxiIn;
    public override ControllerUnit? Unit(PhaseContext ctx) => ctx.Airspace.ArrivalGroundUnit;

    public override PhaseResult OnPilotIntent(PhaseContext ctx, PilotIntent intent)
    {
        if (intent is not (CheckIn or RequestTaxi or ReportVacated) || !CallsMe(ctx)) return PhaseResult.Unhandled;
        if (_taxi is not null) return PhaseResult.Stay;

        string? stand = ctx.Plan.DestinationStand;
        var readback = stand is null
            ? null
            : ReadbackExpectation.Builder().Add(ReadbackItem.Stand, ReadbackValues.Normalize(stand), new TaxiToStand(stand)).Build();
        _taxi = ctx.Conversation.Transmit(Unit(ctx)!, [new TaxiToStand(stand)], TransmissionPriority.Reply, ctx.Now + ctx.Conversation.ReplyDelay(), readback,
            onSent: () => ctx.Clearance.Taxi = new TaxiClearance(stand is null ? "parking" : $"stand {stand}", null, [], [], []), tag: "taxi-in");
        return PhaseResult.Stay;
    }

    public override PhaseResult Tick(PhaseContext ctx)
    {
        var s = ctx.State;
        bool sent = _taxi is { State: not ExchangeState.Queued };
        return sent && s.OnGround && s.GroundSpeedKt < 0.5 && s.ParkingBrakeSet && ctx.Events.CurrentRunway is null
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
