using OpenSkyAtc.Core.Airspace;
using OpenSkyAtc.Core.Brain;
using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Events;
using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Messages;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Planning;
using OpenSkyAtc.Core.Radio;
using OpenSkyAtc.Core.Random;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Core.Phases;

/// <summary>
/// One flight phase. Each owns one controller unit, the requests the pilot can make, and an exit
/// condition. Transitions are returned, never set directly, so the brain logs every change in one place.
/// </summary>
/// <remarks>
/// Beyond the design doc's interface: <see cref="Unit"/> and <see cref="AvailableIntents"/> drive routing
/// and the UI buttons, and <see cref="OnPilotIntent"/> may return <see cref="PhaseResult.Unhandled"/>
/// so the brain answers "unable" or "say again".
/// </remarks>
public interface IPhase
{
    FlightPhase Id { get; }
    ControllerUnit? Unit(PhaseContext ctx);
    void OnEnter(PhaseContext ctx);
    PhaseResult Tick(PhaseContext ctx);                       // may return a transition
    PhaseResult OnPilotIntent(PhaseContext ctx, PilotIntent intent);
    void OnExit(PhaseContext ctx);
    IEnumerable<PilotIntent> AvailableIntents(PhaseContext ctx);
}

/// <param name="Handled">False when the phase does not handle the intent; the brain then answers it.</param>
/// <param name="Transition">Phase to move to, if any.</param>
/// <param name="Redispatch">Pass the same intent to the new phase (e.g. a check-in that starts it).</param>
public sealed record PhaseResult(bool Handled, FlightPhase? Transition = null, string? Reason = null, bool Redispatch = false)
{
    public static readonly PhaseResult Stay = new(true);
    public static readonly PhaseResult Unhandled = new(false);

    public static PhaseResult To(FlightPhase phase, string reason, bool redispatch = false) => new(true, phase, reason, redispatch);
}

/// <summary>Everything a phase can see and use for one call. Built fresh by the brain for each call.</summary>
public sealed record PhaseContext(
    SimState State, FlightSession Session, Clearance Clearance,
    EventEngine Events, TransmissionQueue Radio, AirspaceModel Airspace,
    IRandom Rng)
{
    /// <summary>Events fired by this tick's snapshot (empty for pilot-intent calls).</summary>
    public required IReadOnlyList<SimEvent> NewEvents { get; init; }

    public required ConversationManager Conversation { get; init; }
    public required FlightPlan Plan { get; init; }
    public required AirportData Origin { get; init; }
    public required AirportData Destination { get; init; }
    public required AtcBrainOptions Options { get; init; }
    public required FlightPlanning Planning { get; init; }

    /// <summary>The airports' ATIS, for "confirm information" (null in tests that build a context by hand).</summary>
    public Atis.AtisService? Atis { get; init; }

    /// <summary>Active-clock time (stops while paused). Use it for every delay and timer.</summary>
    public required TimeSpan Now { get; init; }

    /// <summary>The unit the pilot is talking to, for <see cref="IPhase.OnPilotIntent"/> calls.</summary>
    public ControllerUnit? CalledUnit { get; init; }

    public GeoPoint Position => new(State.Latitude, State.Longitude);

    /// <summary>The cleared departure runway end, once the clearance has been sent.</summary>
    public RunwayEnd? DepartureRunway =>
        Clearance.Runway is { } rwy ? Origin.FindRunway(rwy)?.End(rwy) : null;
}

/// <summary>Base class with the common defaults: no-op hooks and registrations cleared on exit.</summary>
public abstract class Phase : IPhase
{
    public abstract FlightPhase Id { get; }
    public abstract ControllerUnit? Unit(PhaseContext ctx);
    public virtual void OnEnter(PhaseContext ctx) { }
    public virtual PhaseResult Tick(PhaseContext ctx) => PhaseResult.Stay;
    public virtual PhaseResult OnPilotIntent(PhaseContext ctx, PilotIntent intent) => PhaseResult.Unhandled;
    public virtual void OnExit(PhaseContext ctx) => ctx.Events.UnregisterAll(this);
    public virtual IEnumerable<PilotIntent> AvailableIntents(PhaseContext ctx) => [];

    /// <summary>True when the pilot is calling this phase's unit.</summary>
    protected bool CallsMe(PhaseContext ctx) => ctx.CalledUnit is { } u && u == Unit(ctx);

    /// <summary>
    /// Check-in reply: "radar contact" (first radar unit) or "identified", plus "confirm altitude" when the
    /// reported altitude differs from Mode C by more than 200 ft (design doc "Check-in").
    /// </summary>
    protected static List<AtcMessagePart> CheckInParts(PhaseContext ctx, PilotIntent intent, bool firstRadarUnit)
    {
        var parts = new List<AtcMessagePart> { firstRadarUnit ? new RadarContact() : new Identified() };
        if (intent is CheckIn { AltitudeFt: { } reported } && Math.Abs(reported - ctx.State.IndicatedAltitudeFt) > 200)
            parts.Add(new ConfirmAltitude());
        return parts;
    }

    /// <summary>A climb clearance to <paramref name="target"/>, parts in <paramref name="before"/> first (e.g. "radar contact").</summary>
    internal static Exchange Climb(PhaseContext ctx, ControllerUnit unit, AltitudeTarget target, TransmissionPriority priority, TimeSpan due,
        IEnumerable<AtcMessagePart>? before = null) =>
        ClearAltitude(ctx, unit, target, priority, due, before, tag: "climb");

    /// <summary>Climb or descent instruction to <paramref name="target"/> (with any extra parts), read back as altitude (and QNH).</summary>
    internal static Exchange ClearAltitude(PhaseContext ctx, ControllerUnit unit, AltitudeTarget target, TransmissionPriority priority, TimeSpan due,
        IEnumerable<AtcMessagePart>? before = null, IEnumerable<AtcMessagePart>? after = null, string tag = "altitude")
    {
        bool climb = target.NominalFeet > ctx.State.IndicatedAltitudeFt;
        AtcMessagePart instruction = climb ? new ClimbTo(target) : new DescendTo(target);
        var parts = (before ?? []).Append(instruction).Concat(after ?? []).ToList();

        var b = ReadbackExpectation.Builder().Add(ReadbackItem.Altitude, ReadbackValues.Altitude(target), instruction);
        if (parts.OfType<Qnh>().FirstOrDefault() is { } qnh) b.Add(ReadbackItem.Qnh, qnh.Hpa.ToString(System.Globalization.CultureInfo.InvariantCulture), qnh);
        return ctx.Conversation.Transmit(unit, parts, priority, due, b.Build(), onSent: () => ctx.Clearance.ClearedAltitude = target, tag: tag);
    }

    /// <summary>Hands off to <paramref name="next"/>: "contact Tower 118.100", expecting the frequency read back.</summary>
    protected static Exchange Handoff(PhaseContext ctx, ControllerUnit from, ControllerUnit next, TimeSpan due, TransmissionPriority priority, string tag)
    {
        var contact = new Contact(next);
        var expectation = ReadbackExpectation.Builder()
            .Add(ReadbackItem.Frequency, ReadbackValues.Frequency(next.FrequencyKhz), contact)
            .Build();
        return ctx.Conversation.Transmit(from, [contact], priority, due, expectation,
            onSent: () => ctx.Clearance.SetHandoff(from, next), tag: tag);
    }
}
