using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Messages;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Planning;
using OpenSkyAtc.Core.Radio;

namespace OpenSkyAtc.Core.Phases;

/// <summary>Phase 1: the pilot requests the IFR clearance from Delivery and must read it back correctly.</summary>
public sealed class PreflightPhase : Phase
{
    private Exchange? _clearance;
    private (RunwayEnd? Runway, AltitudeTarget Climb, int Squawk)? _plan;

    public override FlightPhase Id => FlightPhase.Preflight;
    public override ControllerUnit? Unit(PhaseContext ctx) => ctx.Airspace.ClearanceUnit;

    public override PhaseResult OnPilotIntent(PhaseContext ctx, PilotIntent intent)
    {
        if (intent is not RequestClearance || !CallsMe(ctx)) return PhaseResult.Unhandled;

        var unit = Unit(ctx)!;
        var c = ctx.Clearance;
        _plan ??= (
            RunwaySelector.Select(ctx.Origin, ctx.State.WindDirectionTrueDeg, ctx.State.WindSpeedKt, ctx.Plan.DepartureRunway),
            InitialClimbRule.For(ctx.Origin),
            SquawkAllocator.Allocate(ctx.Options.Squawks, ctx.Origin.Icao, ctx.Rng));
        var (runway, climb, squawk) = _plan.Value;
        string? sid = ctx.Plan.PlannedSid;

        var expectation = ReadbackExpectation.Builder()
            .Add(ReadbackItem.Destination, ReadbackValues.Destination(ctx.Plan.Destination), new ClearedTo(ctx.Plan.Destination, ctx.Destination.Name))
            .Add(ReadbackItem.Route, ReadbackValues.Route(sid), new Via(sid))
            .Add(ReadbackItem.InitialClimb, ReadbackValues.Altitude(climb), new ClimbTo(climb))
            .Add(ReadbackItem.Squawk, ReadbackValues.Squawk(squawk), new Squawk(squawk))
            .Build();

        _clearance = ctx.Conversation.Transmit(unit, [new IfrClearance(ctx.Plan.Destination, ctx.Destination.Name, sid, climb, squawk)],
            TransmissionPriority.Reply, ctx.Now + ctx.Conversation.ReplyDelay(), expectation,
            onSent: () =>
            {
                c.Sid = sid;
                c.ClearedAltitude = climb;
                c.Squawk = squawk;
                c.Runway = runway?.Designator;
                c.CurrentFrequencyKhz = unit.FrequencyKhz;
            },
            tag: "ifr-clearance");
        return PhaseResult.Stay;
    }

    public override PhaseResult Tick(PhaseContext ctx)
    {
        if (_clearance?.State != ExchangeState.Correct) return PhaseResult.Stay;

        // "Readback correct" is said only by clearance delivery (design doc), here with the handoff to Ground.
        var unit = Unit(ctx)!;
        var ground = ctx.Airspace.GroundUnit;
        if (ground is not null && ground != unit)
        {
            var contact = new Contact(ground, ContactCondition.WhenReadyForPushAndStart);
            var expectation = ReadbackExpectation.Builder()
                .Add(ReadbackItem.Frequency, ReadbackValues.Frequency(ground.FrequencyKhz), contact)
                .Build();
            ctx.Conversation.Transmit(unit, [new ReadbackCorrect(), contact], TransmissionPriority.Reply,
                ctx.Now + ctx.Conversation.CorrectionDelay(), expectation,
                onSent: () => ctx.Clearance.NextFrequencyKhz = ground.FrequencyKhz, tag: "contact-ground");
        }
        else
        {
            ctx.Conversation.Say(unit, [new ReadbackCorrect()], TransmissionPriority.Reply, ctx.Now + ctx.Conversation.CorrectionDelay());
        }
        return PhaseResult.To(FlightPhase.Cleared, "clearance read back correctly");
    }

    public override IEnumerable<PilotIntent> AvailableIntents(PhaseContext ctx) =>
        _clearance is null || _clearance.State is ExchangeState.Missed or ExchangeState.Dropped ? [new RequestClearance()] : [];
}

/// <summary>Phase 2: cleared, waiting for the push and start request (or a taxi request at a no-push stand).</summary>
public sealed class ClearedPhase : Phase
{
    private Exchange? _push;

    public override FlightPhase Id => FlightPhase.Cleared;
    public override ControllerUnit? Unit(PhaseContext ctx) => ctx.Airspace.GroundUnit;

    public override PhaseResult OnPilotIntent(PhaseContext ctx, PilotIntent intent)
    {
        if (!CallsMe(ctx)) return PhaseResult.Unhandled;
        switch (intent)
        {
            case RequestPushStart when _push is null:
                var unit = Unit(ctx)!;
                _push = ctx.Conversation.Transmit(unit, [new PushStartApproved()], TransmissionPriority.Reply,
                    ctx.Now + ctx.Conversation.ReplyDelay(), onSent: () => ctx.Clearance.CurrentFrequencyKhz = unit.FrequencyKhz, tag: "push-start");
                return PhaseResult.Stay;
            case RequestTaxi:
                return PhaseResult.To(FlightPhase.TaxiOut, "taxi requested without pushback");
            default:
                return PhaseResult.Unhandled;
        }
    }

    public override PhaseResult Tick(PhaseContext ctx) =>
        _push?.State == ExchangeState.Done ? PhaseResult.To(FlightPhase.Pushback, "push and start approved") : PhaseResult.Stay;

    public override IEnumerable<PilotIntent> AvailableIntents(PhaseContext ctx) =>
        _push is null ? [new RequestPushStart(), new RequestTaxi()] : [];
}

/// <summary>Phase 3: pushing back and starting; ends when the pilot requests taxi.</summary>
public sealed class PushbackPhase : Phase
{
    public override FlightPhase Id => FlightPhase.Pushback;
    public override ControllerUnit? Unit(PhaseContext ctx) => ctx.Airspace.GroundUnit;

    public override PhaseResult OnPilotIntent(PhaseContext ctx, PilotIntent intent) =>
        intent is RequestTaxi && CallsMe(ctx) ? PhaseResult.To(FlightPhase.TaxiOut, "taxi requested") : PhaseResult.Unhandled;

    public override IEnumerable<PilotIntent> AvailableIntents(PhaseContext ctx) => [new RequestTaxi()];
}

/// <summary>
/// Phase 4: taxi to the holding point. Within 150 m of it and below 5 kt, Ground hands off to Tower;
/// the phase ends when the pilot calls Tower.
/// </summary>
public sealed class TaxiOutPhase : Phase
{
    private const double NearHoldingPointM = 150;
    private const double StoppedKt = 5;

    private TaxiPlan? _plan;
    private Exchange? _handoff;

    public override FlightPhase Id => FlightPhase.TaxiOut;
    public override ControllerUnit? Unit(PhaseContext ctx) => ctx.Airspace.GroundUnit;

    public override void OnEnter(PhaseContext ctx)
    {
        var unit = Unit(ctx)!;
        if (ctx.DepartureRunway is not { } runway) return;

        var position = new GeoPoint(ctx.State.Latitude, ctx.State.Longitude);
        var plan = _plan = ctx.Options.TaxiPlanner.PlanToHoldingPoint(ctx.Origin, position, runway);
        string rwy = runway.Designator;
        string? hpName = plan.HoldingPoint?.Name;

        var b = ReadbackExpectation.Builder();
        if (hpName is not null) b.Add(ReadbackItem.HoldingPoint, ReadbackValues.HoldingPoint(hpName), new TaxiHoldingPoint(hpName));
        b.Add(ReadbackItem.Runway, ReadbackValues.Runway(rwy), new RunwayIs(rwy));
        if (plan.HoldShorts.Count > 0)
            b.Add(ReadbackItem.HoldShorts, ReadbackValues.HoldShorts(plan.HoldShorts), plan.HoldShorts.Select(h => (AtcMessagePart)new HoldShort(h)).ToArray());

        ctx.Conversation.Transmit(unit, [new TaxiVia(hpName, rwy, plan.Via, plan.HoldShorts)], TransmissionPriority.Reply,
            ctx.Now + ctx.Conversation.ReplyDelay(), b.Build(),
            onSent: () =>
            {
                ctx.Clearance.Taxi = new TaxiClearance(hpName ?? $"holding point runway {rwy}", rwy, plan.Via, plan.HoldShorts, plan.Path);
                ctx.Clearance.CurrentFrequencyKhz = unit.FrequencyKhz;
            },
            tag: "taxi");
    }

    public override PhaseResult Tick(PhaseContext ctx)
    {
        if (_plan is null || _handoff is not null || ctx.Airspace.OriginTower is not { } tower) return PhaseResult.Stay;

        // The design doc's exit condition is numeric (150 m, 5 kt), so it is read directly here.
        double distance = GeoMath.DistanceM(new GeoPoint(ctx.State.Latitude, ctx.State.Longitude), _plan.Target);
        if (distance <= NearHoldingPointM && ctx.State.GroundSpeedKt < StoppedKt)
            _handoff = Handoff(ctx, Unit(ctx)!, tower, ctx.Now, TransmissionPriority.Instruction, "contact-tower");
        return PhaseResult.Stay;
    }

    public override PhaseResult OnPilotIntent(PhaseContext ctx, PilotIntent intent)
    {
        if (intent is ReadyForDeparture or CheckIn && ctx.CalledUnit is { } u && u == ctx.Airspace.OriginTower)
            return PhaseResult.To(FlightPhase.Holding, _handoff is null ? "called tower before the handoff" : "checked in with tower", redispatch: true);
        return PhaseResult.Unhandled;
    }

    public override IEnumerable<PilotIntent> AvailableIntents(PhaseContext ctx) =>
        _handoff is { State: not ExchangeState.Queued } ? [new ReadyForDeparture()] : [];
}
