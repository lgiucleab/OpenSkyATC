using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Events;
using OpenSkyAtc.Core.Messages;

namespace OpenSkyAtc.Core.Model;

/// <summary>The fifteen phases from the design doc, gate to gate.</summary>
public enum FlightPhase
{
    Preflight = 1,
    Cleared,
    Pushback,
    TaxiOut,
    Holding,
    TakeoffRoll,
    InitialClimb,
    Departure,
    EnRoute,
    Descent,
    Approach,
    Final,
    LandingRoll,
    TaxiIn,
    Parked,
}

/// <summary>Everything the brain tracks about the flight. Read-only outside Core.</summary>
/// <remarks>Exchanges and pending transmissions live in the conversation manager and queue; step 5 adds warnings issued.</remarks>
public sealed class FlightSession(int seed)
{
    public int Seed { get; } = seed;
    public TimeSpan SimTime { get; internal set; }
    public FlightPhase Phase { get; internal set; } = FlightPhase.Preflight;
    public ControllerUnit? CurrentUnit { get; internal set; }
    public TimeSpan? LastPilotTransmission { get; internal set; }
    public Clearance Clearance { get; } = new();
    public EventLog Log { get; } = new();
}

/// <summary>One entry in the event log: every transmission, pilot action and detected event, with sim time.</summary>
public abstract record LogEntry(TimeSpan Time);

public sealed record SimEventLogged(SimEvent Event) : LogEntry(Event.Time);
public sealed record AtcTransmissionLogged(AtcMessage Message) : LogEntry(Message.Time);
/// <param name="FrequencyKhz">Frequency the pilot transmitted on.</param>
/// <param name="Unit">Unit on that frequency; null when nobody is there (the call goes unanswered).</param>
public sealed record PilotIntentLogged(TimeSpan Time, PilotIntent Intent, int FrequencyKhz, ControllerUnit? Unit) : LogEntry(Time);

public sealed record PhaseChanged(TimeSpan Time, FlightPhase From, FlightPhase To, string Reason) : LogEntry(Time);
public sealed record ReadbackChecked(TimeSpan Time, int ExchangeId, string? Tag, ReadbackVerdict Verdict) : LogEntry(Time);
public sealed record ReadbackMissed(TimeSpan Time, int ExchangeId, string? Tag) : LogEntry(Time);

/// <summary>A queued transmission was dropped because the aircraft left the frequency first.</summary>
public sealed record TransmissionDropped(TimeSpan Time, ControllerUnit Unit, IReadOnlyList<AtcMessagePart> Parts) : LogEntry(Time);

/// <summary>The pilot called <paramref name="Called"/> instead of the unit the handoff was to (design doc edge case).</summary>
public sealed record HandoffSkipped(TimeSpan Time, ControllerUnit Expected, ControllerUnit Called) : LogEntry(Time);

/// <summary>A pilot request the current phase does not handle, with how ATC answered.</summary>
public sealed record RequestRejected(TimeSpan Time, PilotIntent Intent, string Reason) : LogEntry(Time);

/// <summary>Append-only log of the session. It is the debugging tool and the replay record.</summary>
public sealed class EventLog
{
    private readonly List<LogEntry> _entries = [];

    public IReadOnlyList<LogEntry> Entries => _entries;

    internal void Add(LogEntry entry) => _entries.Add(entry);
}
