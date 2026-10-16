namespace OpenSkyAtc.Core.Brain;

// Minimal records so IAtcBrain can be declared in step 1. Steps 2-4 replace these
// with the full models from the design doc (data model, conversation model, message parts).

/// <summary>Flight plan. Step 2 adds route, cruise level, SID/STAR and runway hints.</summary>
public sealed record FlightPlan(string Callsign, string Origin, string Destination);

/// <summary>Airport data from SimConnect facilities or JSON fixtures. Later steps add runways, frequencies and the taxi graph.</summary>
public sealed record AirportData(string Icao);

/// <summary>Pilot intents from the design doc's catalog. Slots (altitudes, fixes, readback items) are added in step 3.</summary>
public enum PilotIntentKind
{
    RequestClearance,
    RequestPushStart,
    RequestTaxi,
    ReadyForDeparture,
    CheckIn,
    RequestLevel,
    RequestDirect,
    RequestDescent,
    ReportEstablished,
    GoingAround,
    ReportVacated,
    Readback,
    SayAgain,
    Wilco,
    Unable,
    Mayday,
    PanPan,
}

public sealed record PilotIntent(PilotIntentKind Kind);

/// <summary>A part of an ATC message. Step 4 adds the typed parts (ClimbTo, Squawk, Contact, ...).</summary>
public abstract record AtcMessagePart;

/// <summary>Unstructured text, used only by the step 1 stub brain.</summary>
public sealed record TextPart(string Text) : AtcMessagePart;

public sealed record AtcMessage(TimeSpan Time, string Unit, string Callsign, IReadOnlyList<AtcMessagePart> Parts);

/// <summary>Session state. Step 2 adds phase, controller unit, exchanges and the event log.</summary>
public sealed class FlightSession(int seed)
{
    public int Seed { get; } = seed;
    public TimeSpan SimTime { get; internal set; }
}
