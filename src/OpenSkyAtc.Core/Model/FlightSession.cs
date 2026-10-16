using OpenSkyAtc.Core.Brain;
using OpenSkyAtc.Core.Events;

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
/// <remarks>Step 3 adds the exchange in progress and pending ATC actions; step 5 adds warnings issued.</remarks>
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
public sealed record PilotIntentLogged(TimeSpan Time, PilotIntent Intent) : LogEntry(Time);

/// <summary>Append-only log of the session. It is the debugging tool and the replay record.</summary>
public sealed class EventLog
{
    private readonly List<LogEntry> _entries = [];

    public IReadOnlyList<LogEntry> Entries => _entries;

    internal void Add(LogEntry entry) => _entries.Add(entry);
}
