using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Events;
using OpenSkyAtc.Core.Messages;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Planning;
using OpenSkyAtc.Core.Radio;

namespace OpenSkyAtc.Core.Phases;

/// <summary>
/// Phase 5: at the holding point with Tower. On "ready for departure" Tower either clears for takeoff
/// directly or says "line up and wait" and clears for takeoff 20-60 s after the line-up readback.
/// Ends when the takeoff roll starts on the departure runway.
/// </summary>
public sealed class HoldingPhase : Phase
{
    private Exchange? _lineUp;
    private Exchange? _takeoff;
    private TimeSpan? _takeoffDue;

    public override FlightPhase Id => FlightPhase.Holding;
    public override ControllerUnit? Unit(PhaseContext ctx) => ctx.Airspace.OriginTower;

    public override PhaseResult OnPilotIntent(PhaseContext ctx, PilotIntent intent)
    {
        if (intent is not (ReadyForDeparture or CheckIn) || !CallsMe(ctx)) return PhaseResult.Unhandled;
        if (_lineUp is not null || _takeoff is not null) return PhaseResult.Stay;
        if (ctx.Clearance.Runway is not { } rwy) return PhaseResult.Unhandled;

        var unit = Unit(ctx)!;
        ctx.Clearance.CurrentFrequencyKhz = unit.FrequencyKhz;
        TimeSpan due = ctx.Now + ctx.Conversation.ReplyDelay();

        if (ctx.Rng.NextDouble() < ctx.Options.LineUpAndWaitProbability)
        {
            var expectation = ReadbackExpectation.Builder()
                .Add(ReadbackItem.Runway, ReadbackValues.Runway(rwy), new RunwayIs(rwy))
                .Add(ReadbackItem.Clearance, ReadbackValues.LineUp, new LineUpAndWait(rwy))
                .Build();
            _lineUp = ctx.Conversation.Transmit(unit, [new LineUpAndWait(rwy)], TransmissionPriority.Reply, due, expectation,
                onSent: () => ctx.Clearance.LineUpCleared = rwy, tag: "line-up");
        }
        else
        {
            _takeoff = ClearForTakeoff(ctx, unit, rwy, TransmissionPriority.Reply, due);
        }
        return PhaseResult.Stay;
    }

    public override PhaseResult Tick(PhaseContext ctx)
    {
        if (_lineUp is { State: ExchangeState.Correct or ExchangeState.Missed } && _takeoffDue is null && _takeoff is null)
        {
            var (min, max) = ctx.Options.LineUpToTakeoffSeconds;
            _takeoffDue = ctx.Now + TimeSpan.FromSeconds(Math.Round(min + ctx.Rng.NextDouble() * (max - min), 1));
        }
        if (_takeoffDue is { } due && due <= ctx.Now && _takeoff is null && ctx.Clearance.Runway is { } rwy)
            _takeoff = ClearForTakeoff(ctx, Unit(ctx)!, rwy, TransmissionPriority.Instruction, ctx.Now);

        foreach (var roll in ctx.NewEvents.OfType<TakeoffRoll>())
        {
            if (ctx.Clearance.Runway is { } r && roll.Runway.Has(r))
                return PhaseResult.To(FlightPhase.TakeoffRoll, $"takeoff roll on runway {r}");
        }
        return PhaseResult.Stay;
    }

    public override IEnumerable<PilotIntent> AvailableIntents(PhaseContext ctx) =>
        _lineUp is null && _takeoff is null ? [new ReadyForDeparture()] : [];

    private static Exchange ClearForTakeoff(PhaseContext ctx, ControllerUnit unit, string rwy, TransmissionPriority priority, TimeSpan due)
    {
        var expectation = ReadbackExpectation.Builder()
            .Add(ReadbackItem.Runway, ReadbackValues.Runway(rwy), new RunwayIs(rwy))
            .Add(ReadbackItem.Clearance, ReadbackValues.Takeoff, new ClearedTakeoff(rwy))
            .Build();
        return ctx.Conversation.Transmit(unit, [WindReport.From(ctx.State), new ClearedTakeoff(rwy)], priority, due, expectation,
            onSent: () =>
            {
                ctx.Clearance.LineUpCleared = rwy;
                ctx.Clearance.TakeoffCleared = rwy;
            },
            tag: "takeoff");
    }
}

/// <summary>Phase 6: takeoff roll. Tower is silent; ends when airborne.</summary>
public sealed class TakeoffRollPhase : Phase
{
    public override FlightPhase Id => FlightPhase.TakeoffRoll;
    public override ControllerUnit? Unit(PhaseContext ctx) => ctx.Airspace.OriginTower;

    public override PhaseResult Tick(PhaseContext ctx) =>
        ctx.NewEvents.OfType<Airborne>().Any() ? PhaseResult.To(FlightPhase.InitialClimb, "airborne") : PhaseResult.Stay;

    public override PhaseResult OnPilotIntent(PhaseContext ctx, PilotIntent intent)
    {
        if (intent is not RejectTakeoff || !CallsMe(ctx)) return PhaseResult.Unhandled;
        ctx.Clearance.TakeoffCleared = null;
        ctx.Conversation.Say(Unit(ctx)!, [new Roger()], TransmissionPriority.Reply, ctx.Now + ctx.Conversation.CorrectionDelay());
        return PhaseResult.To(FlightPhase.Holding, "takeoff rejected");
    }

    public override IEnumerable<PilotIntent> AvailableIntents(PhaseContext ctx) => [new RejectTakeoff()];
}

/// <summary>Phase 7: initial climb. Tower hands off to Departure at about 1,000 ft AGL; ends when the pilot checks in there.</summary>
public sealed class InitialClimbPhase : Phase
{
    public const double HandoffAglFt = 1000;

    private AltitudeRegistration? _handoffAltitude;
    private Exchange? _handoff;

    public override FlightPhase Id => FlightPhase.InitialClimb;
    public override ControllerUnit? Unit(PhaseContext ctx) => ctx.Airspace.OriginTower;

    public override void OnEnter(PhaseContext ctx) =>
        _handoffAltitude = ctx.Events.RegisterAltitude(this, HandoffAglFt, AltitudeReference.AboveGround, "contact-departure");

    public override PhaseResult Tick(PhaseContext ctx)
    {
        bool reached = ctx.NewEvents.OfType<AltitudeCrossed>().Any(e => e.Registration == _handoffAltitude && e.Climbing);
        if (reached && _handoff is null && ctx.Airspace.DepartureUnit is { } departure)
            _handoff = Handoff(ctx, Unit(ctx)!, departure, ctx.Now, TransmissionPriority.Instruction, "contact-departure");
        return PhaseResult.Stay;
    }

    public override PhaseResult OnPilotIntent(PhaseContext ctx, PilotIntent intent)
    {
        if (intent is CheckIn && ctx.CalledUnit is { } u && u == ctx.Airspace.DepartureUnit)
            return PhaseResult.To(FlightPhase.Departure, _handoff is null ? "checked in with departure before the handoff" : "checked in with departure", redispatch: true);
        return PhaseResult.Unhandled;
    }

    public override IEnumerable<PilotIntent> AvailableIntents(PhaseContext ctx) =>
        _handoff is { State: not ExchangeState.Queued }
            ? [new CheckIn((int)(Math.Round(ctx.State.IndicatedAltitudeFt / 100) * 100), ctx.Clearance.ClearedAltitude)]
            : [];
}

/// <summary>Phase 8 placeholder until step 6: accepts the check-in so the handoff can be verified.</summary>
public sealed class DeparturePhase : Phase
{
    public override FlightPhase Id => FlightPhase.Departure;
    public override ControllerUnit? Unit(PhaseContext ctx) => ctx.Airspace.DepartureUnit;

    public override PhaseResult OnPilotIntent(PhaseContext ctx, PilotIntent intent)
    {
        if (intent is not CheckIn || !CallsMe(ctx)) return PhaseResult.Unhandled;
        ctx.Clearance.CurrentFrequencyKhz = Unit(ctx)!.FrequencyKhz;
        return PhaseResult.Stay;
    }
}
