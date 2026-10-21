using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Messages;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Planning;
using OpenSkyAtc.Core.Radio;

namespace OpenSkyAtc.Core.Phases;

/// <summary>
/// "Request runway 14 for departure" (refactor phase 6, owner's choices). A runway end is approved when the wind
/// allows it (no more crosswind than the runway rules allow, and no more tailwind than the flight plan's own runway
/// may have). Before taxi, ATC gives a new clearance with that runway's SID, read back like the first:
/// "roger, runway 14, cleared to ... via ... departure, climb ..., squawk ...". While taxiing, a new taxi clearance
/// to that runway (CAP 413), only if the SID stays the same. Otherwise "unable approve runway 14".
/// </summary>
internal static class RunwayRequests
{
    /// <summary>Other runway ends the wind allows now, for the call buttons.</summary>
    public static IEnumerable<RunwayEnd> Options(PhaseContext ctx) =>
        ctx.Origin.Runways.SelectMany(r => new[] { r.First, r.Second })
            .Where(e => !string.Equals(e.Designator, ctx.Clearance.Runway, StringComparison.OrdinalIgnoreCase) && Acceptable(ctx, e))
            .OrderBy(e => e.Designator, StringComparer.Ordinal);

    public static RunwayEnd? Find(PhaseContext ctx, string designator) =>
        ctx.Origin.FindRunway(designator)?.End(designator);

    public static bool Acceptable(PhaseContext ctx, RunwayEnd end)
    {
        var rules = ctx.Options.Planning.Runways;
        double cross = Math.Abs(RunwaySelector.CrosswindKt(end, ctx.State.WindDirectionTrueDeg, ctx.State.WindSpeedKt));
        double head = RunwaySelector.HeadwindKt(end, ctx.State.WindDirectionTrueDeg, ctx.State.WindSpeedKt);
        return cross <= rules.MaxCrosswindKt && -head <= rules.MaxPlanTailwindKt;
    }

    /// <summary>Before taxi: a new clearance for the runway, or "unable".</summary>
    public static void Reclear(PhaseContext ctx, ControllerUnit unit, string designator)
    {
        if (Find(ctx, designator) is not { } end || !Acceptable(ctx, end))
        {
            Unable(ctx, unit, designator);
            return;
        }
        var c = ctx.Clearance;
        ctx.Planning.ApproveDepartureRunway(end, ctx.State);
        string? sid = SidChooser.Choose(ctx.Origin, ctx.Plan.PlannedSid, end);
        var climb = InitialClimbRule.For(ctx.Origin, sid);
        int squawk = c.Squawk ?? SquawkAllocator.Allocate(ctx.Options.Squawks, ctx.Origin.Icao, ctx.Rng);
        var expectation = ReadbackExpectation.Builder()
            .Add(ReadbackItem.Runway, ReadbackValues.Runway(end.Designator), new RunwayIs(end.Designator))
            .Add(ReadbackItem.Route, ReadbackValues.Route(sid), new Via(sid))
            .Add(ReadbackItem.InitialClimb, ReadbackValues.Altitude(climb), new ClimbTo(climb))
            .Add(ReadbackItem.Squawk, ReadbackValues.Squawk(squawk), new Squawk(squawk))
            .Build();
        ctx.Conversation.Transmit(unit, [new Roger(), new RunwayIs(end.Designator), new IfrClearance(ctx.Plan.Destination, ctx.Destination.Name, sid, climb, squawk)],
            TransmissionPriority.Reply, ctx.Now + ctx.Conversation.ReplyDelay(), expectation,
            onSent: () =>
            {
                c.Runway = end.Designator;
                c.Sid = sid;
                c.ClearedAltitude = climb;
                c.Squawk = squawk;
            },
            tag: "ifr-clearance");
    }

    /// <summary>While taxiing: the runway, if it keeps the cleared SID (then the caller re-issues the taxi), else null after "unable".</summary>
    public static RunwayEnd? ForTaxi(PhaseContext ctx, ControllerUnit unit, string designator)
    {
        if (Find(ctx, designator) is { } end && Acceptable(ctx, end) && SidChooser.Choose(ctx.Origin, ctx.Plan.PlannedSid, end) == ctx.Clearance.Sid)
        {
            ctx.Planning.ApproveDepartureRunway(end, ctx.State);
            return end;
        }
        Unable(ctx, unit, designator);
        return null;
    }

    private static void Unable(PhaseContext ctx, ControllerUnit unit, string designator) =>
        ctx.Conversation.Say(unit, [new UnableApproveRunway(ReadbackValues.Runway(designator))], TransmissionPriority.Reply, ctx.Now + ctx.Conversation.ReplyDelay());
}
