namespace OpenSkyAtc.Core.Brain;

// Minimal records so IAtcBrain can be declared before the conversation model (step 3)
// and the message parts (step 4) exist.

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
