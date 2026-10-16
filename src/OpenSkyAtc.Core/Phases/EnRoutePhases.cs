using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Events;
using OpenSkyAtc.Core.Messages;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Radio;

namespace OpenSkyAtc.Core.Phases;

/// <summary>
/// Issues the planned climb steps (design doc "Step climbs"): after a level-off at, or passing 2,000 ft below,
/// the cleared level, wait 30-120 s (sometimes longer, "traffic"), then clear the next step. A pilot request
/// for higher gets the step at once, or "stand by" during a traffic hold-down.
/// </summary>
internal sealed class StepClimber(object owner)
{
    private AltitudeTarget? _registeredFor;
    private AltitudeRegistration? _below;
    private bool _pilotAsked;

    public void Tick(PhaseContext ctx, ControllerUnit unit)
    {
        var cleared = ctx.Clearance.ClearedAltitude;
        if (cleared != _registeredFor)
        {
            if (_below is not null) ctx.Events.Unregister(_below);
            _below = cleared is { } c ? ctx.Events.RegisterAltitude(owner, c.NominalFeet - 2000, tag: "step-climb") : null;
            _registeredFor = cleared;
        }
        if (cleared is null || ctx.Planning.NextClimbStep(cleared) is null) return;

        foreach (var e in ctx.NewEvents)
        {
            bool trigger = e switch
            {
                AltitudeCrossed a => a.Registration == _below && a.Climbing,
                LevelOff l => Math.Abs(l.AltitudeFt - cleared.Value.NominalFeet) <= 300,
                _ => false,
            };
            if (trigger) ctx.Planning.ScheduleStep(ctx.Now);
        }

        bool due = ctx.Planning.StepDue(ctx.Now) || (_pilotAsked && !ctx.Planning.HoldingDown);
        if (due && !ctx.Conversation.IsBusy(unit) && ctx.Clearance.HandoffTo is null) Issue(ctx, unit, TransmissionPriority.Instruction, ctx.Now);
    }

    /// <summary>Pilot asks for higher: the next step now, or "stand by" while held down.</summary>
    public void Request(PhaseContext ctx, ControllerUnit unit)
    {
        if (ctx.Planning.NextClimbStep(ctx.Clearance.ClearedAltitude) is null)
        {
            ctx.Conversation.Say(unit, [new SayAgainRequest()], TransmissionPriority.Reply, ctx.Now + ctx.Conversation.ReplyDelay());
            return;
        }
        if (ctx.Planning.HoldingDown)
        {
            _pilotAsked = true;
            ctx.Conversation.Say(unit, [new StandBy()], TransmissionPriority.Reply, ctx.Now + ctx.Conversation.CorrectionDelay());
            return;
        }
        Issue(ctx, unit, TransmissionPriority.Reply, ctx.Now + ctx.Conversation.ReplyDelay());
    }

    public void Issue(PhaseContext ctx, ControllerUnit unit, TransmissionPriority priority, TimeSpan due, IEnumerable<AtcMessagePart>? before = null)
    {
        if (ctx.Planning.NextClimbStep(ctx.Clearance.ClearedAltitude) is not { } next) return;
        Phase.Climb(ctx, unit, next, priority, due, before);
        ctx.Planning.StepIssued();
        _pilotAsked = false;
    }
}

/// <summary>Shared by the area-control phases: the unit in control, check-ins, sector handoffs, direct-to, level requests.</summary>
public abstract class AreaPhase : Phase
{
    private readonly StepClimber _climber;
    private ControllerUnit? _unit;
    private Exchange? _handoff;
    private ControllerUnit? _handedOffTo;

    protected AreaPhase() => _climber = new StepClimber(this);

    /// <summary>True once the pilot has checked in with the current unit.</summary>
    protected bool CheckedIn { get; set; }

    public override ControllerUnit? Unit(PhaseContext ctx) => _unit ?? ctx.Session.CurrentUnit ?? ctx.Airspace.EnRouteUnitAt(ctx.Position);

    public override void OnEnter(PhaseContext ctx)
    {
        _unit = ctx.Session.CurrentUnit;
        CheckedIn = _unit is not null && ctx.Clearance.HandoffTo is null && ctx.State.IsTunedTo(_unit);
    }

    /// <summary>Hands off to the next CTR sector 5 nm before the boundary.</summary>
    protected void SectorHandoffs(PhaseContext ctx)
    {
        if (!CheckedIn || _handoff is { State: ExchangeState.Queued or ExchangeState.AwaitingReadback } || ctx.Clearance.HandoffTo is not null) return;
        var sector = ctx.Airspace.Sectors.FirstOrDefault(s => s.Unit == _unit);
        if (sector is null || ctx.Airspace.SectorAfter(sector) is not { } next) return;
        if (_handedOffTo == next.Unit) return; // already handed off; waiting for the check-in
        if (ctx.Airspace.Route.AlongNm(ctx.Position) >= sector.EndNm - ctx.Airspace.Options.HandoffBeforeBoundaryNm)
        {
            _handoff = Handoff(ctx, _unit!, next.Unit, ctx.Now, TransmissionPriority.Instruction, "contact-ctr");
            _handedOffTo = next.Unit;
        }
    }

    protected void HandOffTo(PhaseContext ctx, ControllerUnit next, string tag)
    {
        if (_handedOffTo == next) return;
        _handoff = Handoff(ctx, _unit!, next, ctx.Now, TransmissionPriority.Instruction, tag);
        _handedOffTo = next;
    }

    protected bool HandoffIssued(string tag) => _handoff?.Tag == tag;

    protected void Climbs(PhaseContext ctx)
    {
        if (CheckedIn && _unit is not null) _climber.Tick(ctx, _unit);
    }

    /// <summary>Handles check-in on a CTR/Departure unit, direct-to and level requests. Returns null when not handled.</summary>
    protected PhaseResult? AreaIntents(PhaseContext ctx, PilotIntent intent, bool firstRadarUnit)
    {
        var called = ctx.CalledUnit;
        bool areaUnit = called is not null && (called == ctx.Airspace.DepartureUnit || ctx.Airspace.Sectors.Any(s => s.Unit == called));
        switch (intent)
        {
            case CheckIn when areaUnit && (!CheckedIn || called != _unit):
                _unit = called;
                CheckedIn = true;
                OnCheckedIn(ctx, intent, firstRadarUnit);
                return PhaseResult.Stay;
            case RequestLevel when called == _unit && CheckedIn:
                _climber.Request(ctx, _unit!);
                return PhaseResult.Stay;
            case RequestDirect d when called == _unit && CheckedIn:
                var direct = new Direct(d.Fix);
                var readback = ReadbackExpectation.Builder().Add(ReadbackItem.DirectTo, ReadbackValues.Normalize(d.Fix), direct).Build();
                ctx.Conversation.Transmit(_unit!, [direct], TransmissionPriority.Reply, ctx.Now + ctx.Conversation.ReplyDelay(), readback,
                    onSent: () =>
                    {
                        ctx.Clearance.DirectTo = d.Fix;
                        ctx.Clearance.AssignedHeadingDeg = null;
                    }, tag: "direct");
                return PhaseResult.Stay;
            default:
                return null;
        }
    }

    /// <summary>Reply to a check-in: "identified" (or "radar contact") and the next step if one is due.</summary>
    protected virtual void OnCheckedIn(PhaseContext ctx, PilotIntent intent, bool firstRadarUnit)
    {
        var parts = CheckInParts(ctx, intent, firstRadarUnit);
        ctx.Conversation.Say(_unit!, parts, TransmissionPriority.Reply, ctx.Now + ctx.Conversation.CheckInDelay());
    }

    private protected StepClimber Climber => _climber;
    protected ControllerUnit? CurrentUnit => _unit;

    protected IEnumerable<PilotIntent> AreaAvailable(PhaseContext ctx)
    {
        var list = new List<PilotIntent>();
        if (!CheckedIn || ctx.Clearance.HandoffTo is not null)
            list.Add(new CheckIn((int)(Math.Round(ctx.State.IndicatedAltitudeFt / 100) * 100), ctx.Clearance.ClearedAltitude));
        if (CheckedIn && ctx.Planning.NextClimbStep(ctx.Clearance.ClearedAltitude) is { } next) list.Add(new RequestLevel(next));
        if (CheckedIn && NextWaypoint(ctx) is { } wp) list.Add(new RequestDirect(wp));
        return list;
    }

    private static string? NextWaypoint(PhaseContext ctx)
    {
        double along = ctx.Airspace.Route.AlongNm(ctx.Position);
        var route = ctx.Plan.Route;
        if (route.Count == 0) return null;
        // The second waypoint ahead, so "direct" actually shortens the route.
        var ahead = route.Where(w => ctx.Airspace.Route.AlongNm(w.Position) > along + 1).Select(w => w.Ident).ToList();
        return ahead.Count >= 2 ? ahead[1] : ahead.FirstOrDefault();
    }
}

/// <summary>
/// Phase 8: Departure. "Radar contact" and the first climb step on check-in, then step climbs and direct-to.
/// Above FL150 or 40 nm from the origin, hands off to the first CTR sector (or carries on when there is none).
/// </summary>
public sealed class DeparturePhase : AreaPhase
{
    public override FlightPhase Id => FlightPhase.Departure;
    public override ControllerUnit? Unit(PhaseContext ctx) => ctx.Airspace.DepartureUnit;

    public override void OnEnter(PhaseContext ctx) => CheckedIn = false;

    public override PhaseResult OnPilotIntent(PhaseContext ctx, PilotIntent intent)
    {
        if (ctx.Airspace.Sectors.FirstOrDefault() is { } first && intent is CheckIn && ctx.CalledUnit == first.Unit)
            return PhaseResult.To(FlightPhase.EnRoute, HandoffIssued("contact-ctr") ? "checked in with area control" : "called area control before the handoff", redispatch: true);
        return AreaIntents(ctx, intent, firstRadarUnit: true) ?? PhaseResult.Unhandled;
    }

    protected override void OnCheckedIn(PhaseContext ctx, PilotIntent intent, bool firstRadarUnit)
    {
        var parts = CheckInParts(ctx, intent, firstRadarUnit);
        if (ctx.Planning.NextClimbStep(ctx.Clearance.ClearedAltitude) is not null)
            Climber.Issue(ctx, CurrentUnit!, TransmissionPriority.Reply, ctx.Now + ctx.Conversation.CheckInDelay(), parts);
        else
            ctx.Conversation.Say(CurrentUnit!, parts, TransmissionPriority.Reply, ctx.Now + ctx.Conversation.CheckInDelay());
    }

    public override PhaseResult Tick(PhaseContext ctx)
    {
        Climbs(ctx);
        if (!CheckedIn) return PhaseResult.Stay;

        bool leaving = ctx.State.IndicatedAltitudeFt >= ctx.Airspace.Options.TerminalCeilingFt
                       || ctx.Airspace.Route.AlongNm(ctx.Position) >= ctx.Airspace.Options.TerminalRadiusNm;
        if (!leaving) return PhaseResult.Stay;

        if (ctx.Airspace.Sectors.FirstOrDefault() is { } first)
        {
            HandOffTo(ctx, first.Unit, "contact-ctr");
            return PhaseResult.Stay;
        }
        return PhaseResult.To(FlightPhase.EnRoute, "no CTR sectors on this route: Departure keeps the aircraft");
    }

    public override IEnumerable<PilotIntent> AvailableIntents(PhaseContext ctx) => AreaAvailable(ctx);
}

/// <summary>Phase 9: En route with CTR (or Departure on short routes). Step climbs to cruise, sector handoffs, until the descent point.</summary>
public sealed class EnRoutePhase : AreaPhase
{
    private bool _descentRequested;

    public override FlightPhase Id => FlightPhase.EnRoute;

    public override PhaseResult OnPilotIntent(PhaseContext ctx, PilotIntent intent)
    {
        if (intent is RequestDescent && ctx.CalledUnit == CurrentUnit && CheckedIn)
        {
            if (ctx.Planning.DescentRequestGranted(ctx.Airspace.Route.RemainingNm(ctx.Position)))
                return PhaseResult.To(FlightPhase.Descent, "descent requested near the descent point");
            _descentRequested = true;
            ctx.Conversation.Say(CurrentUnit!, [new StandBy()], TransmissionPriority.Reply, ctx.Now + ctx.Conversation.CorrectionDelay());
            return PhaseResult.Stay;
        }
        return AreaIntents(ctx, intent, firstRadarUnit: false) ?? PhaseResult.Unhandled;
    }

    public override PhaseResult Tick(PhaseContext ctx)
    {
        Climbs(ctx);
        SectorHandoffs(ctx);
        if (!CheckedIn || ctx.Clearance.HandoffTo is not null) return PhaseResult.Stay;

        double remaining = ctx.Airspace.Route.RemainingNm(ctx.Position);
        if (ctx.Planning.DescentDue(remaining) || (_descentRequested && ctx.Planning.DescentRequestGranted(remaining)))
            return PhaseResult.To(FlightPhase.Descent, "top of descent");
        return PhaseResult.Stay;
    }

    public override IEnumerable<PilotIntent> AvailableIntents(PhaseContext ctx) =>
        CheckedIn ? AreaAvailable(ctx).Append(new RequestDescent()) : AreaAvailable(ctx);
}

/// <summary>Phase 10: Descent. Descend to FL100 (or the approach altitude), sector handoffs, then hand off to Approach in the arrival volume.</summary>
public sealed class DescentPhase : AreaPhase
{
    private bool _descended;

    public override FlightPhase Id => FlightPhase.Descent;

    public override PhaseResult Tick(PhaseContext ctx)
    {
        if (CheckedIn && !_descended && !ctx.Conversation.IsBusy(CurrentUnit!))
        {
            _descended = true;
            var level = ctx.Planning.DescentLevel;
            var after = level.Kind == AltitudeKind.Altitude ? new[] { (AtcMessagePart)new Qnh(Planning.FlightPlanning.Qnh(ctx.State)) } : null;
            ClearAltitude(ctx, CurrentUnit!, level, TransmissionPriority.Instruction, ctx.Now, after: after, tag: "descent");
        }

        SectorHandoffs(ctx);
        var arrival = ctx.Airspace.ArrivalApproachUnit ?? ctx.Airspace.ArrivalTowerUnit;
        if (CheckedIn && _descended && arrival is not null && ctx.Clearance.HandoffTo is null
            && ctx.Airspace.InArrivalVolume(ctx.Position, ctx.State.IndicatedAltitudeFt))
            HandOffTo(ctx, arrival, "contact-approach");
        return PhaseResult.Stay;
    }

    public override PhaseResult OnPilotIntent(PhaseContext ctx, PilotIntent intent)
    {
        if (intent is CheckIn or ReadyForDeparture && ctx.CalledUnit is { } called)
        {
            if (called == ctx.Airspace.ArrivalApproachUnit) return PhaseResult.To(FlightPhase.Approach, "checked in with approach", redispatch: true);
            if (ctx.Airspace.ArrivalApproachUnit is null && called == ctx.Airspace.ArrivalTowerUnit)
                return PhaseResult.To(FlightPhase.Final, "no approach unit: checked in with tower", redispatch: true);
        }
        return AreaIntents(ctx, intent, firstRadarUnit: false) ?? PhaseResult.Unhandled;
    }

    public override IEnumerable<PilotIntent> AvailableIntents(PhaseContext ctx) => AreaAvailable(ctx);
}
