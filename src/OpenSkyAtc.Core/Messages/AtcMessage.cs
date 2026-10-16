using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Radio;

namespace OpenSkyAtc.Core.Messages;

/// <summary>The aircraft's callsign: the filed code ("VLG1234") and the airline telephony ("Vueling"), if known.</summary>
public sealed record AircraftCallsign(string Code, string? Telephony);

/// <summary>
/// One ATC transmission, as structured parts. The phraseology layer renders it as display text and as
/// speech text; the brain never builds strings.
/// </summary>
public sealed record AtcMessage(
    TimeSpan Time,
    ControllerUnit Unit,
    AircraftCallsign Callsign,
    IReadOnlyList<AtcMessagePart> Parts,
    TransmissionPriority Priority);

/// <summary>A building block of an ATC message. The phraseology layer renders each part.</summary>
public abstract record AtcMessagePart;

/// <summary>"cleared to LEMD via NAPSI1C departure, climb 5000 ft, squawk 4521". A null SID means flight planned route.</summary>
/// <param name="Destination">ICAO code, shown in display text.</param>
/// <param name="DestinationName">Name spoken instead of the code ("Madrid"); null to spell the code.</param>
public sealed record IfrClearance(string Destination, string? DestinationName, string? Sid, AltitudeTarget InitialClimb, int Squawk) : AtcMessagePart;

public sealed record ClearedTo(string Destination, string? DestinationName) : AtcMessagePart;
public sealed record Via(string? Sid) : AtcMessagePart;
public sealed record ClimbTo(AltitudeTarget Altitude) : AtcMessagePart;
public sealed record DescendTo(AltitudeTarget Altitude) : AtcMessagePart;

public enum TurnDirection
{
    /// <summary>"fly heading 270": no turn direction given.</summary>
    None,
    Left,
    Right,
}

public sealed record TurnHeading(TurnDirection Direction, int HeadingDeg) : AtcMessagePart;
public sealed record Direct(string Fix) : AtcMessagePart;

public enum SpeedAction
{
    Maintain,
    Reduce,
    Increase,
}

public sealed record Speed(int Knots, SpeedAction Action = SpeedAction.Maintain) : AtcMessagePart;
public sealed record Qnh(int Hpa) : AtcMessagePart;
public sealed record Squawk(int Code) : AtcMessagePart;
public sealed record ReadbackCorrect : AtcMessagePart;

public enum ContactCondition
{
    None,
    WhenReadyForPushAndStart,
}

/// <summary>"contact Barcelona Tower 118.100". The unit carries the callsign, role and frequency.</summary>
public sealed record Contact(ControllerUnit Unit, ContactCondition Condition = ContactCondition.None) : AtcMessagePart;

public sealed record PushStartApproved : AtcMessagePart;

/// <summary>"taxi to holding point A1 runway 24L via K, M, hold short runway 02". Empty segments is the no-route fallback.</summary>
public sealed record TaxiVia(string? HoldingPoint, string Runway, IReadOnlyList<string> Segments, IReadOnlyList<string> HoldShorts) : AtcMessagePart;

public sealed record TaxiHoldingPoint(string Name) : AtcMessagePart;
public sealed record RunwayIs(string Runway) : AtcMessagePart;
public sealed record HoldShort(string Runway) : AtcMessagePart;
public sealed record LineUpAndWait(string Runway) : AtcMessagePart;

/// <summary>Surface wind in degrees magnetic, rounded to 10 degrees. Speed 0 means calm.</summary>
public sealed record Wind(int DirectionMagDeg, int SpeedKt) : AtcMessagePart;

public sealed record ClearedTakeoff(string Runway) : AtcMessagePart;
public sealed record ClearedLand(string Runway) : AtcMessagePart;

/// <param name="Repeated">True on a second or later correction of the same exchange ("negative, I say again").</param>
public sealed record Negative(bool Repeated = false) : AtcMessagePart;
public sealed record ReadBackRequest(IReadOnlyList<ReadbackItem> Items) : AtcMessagePart;
public sealed record StandBy : AtcMessagePart;
public sealed record SayAgainRequest : AtcMessagePart;
public sealed record UnableContact(ControllerUnit Unit) : AtcMessagePart;
public sealed record Roger : AtcMessagePart;

public enum EmergencyKind
{
    Mayday,
    PanPan,
}

/// <summary>Acknowledgement only in v1 (design doc scope).</summary>
public sealed record EmergencyAcknowledged(EmergencyKind Kind) : AtcMessagePart;
