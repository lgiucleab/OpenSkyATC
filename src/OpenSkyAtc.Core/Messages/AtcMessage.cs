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
/// <param name="Immediately">"descend immediately FL120" (conformance level 2).</param>
public sealed record ClimbTo(AltitudeTarget Altitude, bool Immediately = false) : AtcMessagePart;
public sealed record DescendTo(AltitudeTarget Altitude, bool Immediately = false) : AtcMessagePart;

/// <summary>"check altitude, cleared FL120".</summary>
public sealed record CheckAltitude(AltitudeTarget Cleared) : AtcMessagePart;

/// <summary>"confirm climbing FL240" / "confirm descending FL80".</summary>
public sealed record ConfirmVertical(bool Climbing, AltitudeTarget Altitude) : AtcMessagePart;

public enum TurnDirection
{
    /// <summary>"fly heading 270": no turn direction given.</summary>
    None,
    Left,
    Right,
}

/// <param name="Immediately">"turn left immediately heading 270" (conformance level 2).</param>
public sealed record TurnHeading(TurnDirection Direction, int HeadingDeg, bool Immediately = false) : AtcMessagePart;

/// <summary>"check heading" (followed by the heading).</summary>
public sealed record CheckHeading : AtcMessagePart;

public sealed record SayIntentions : AtcMessagePart;
public sealed record Direct(string Fix) : AtcMessagePart;

public enum SpeedAction
{
    Maintain,
    Reduce,
    Increase,
}

public sealed record Speed(int Knots, SpeedAction Action = SpeedAction.Maintain) : AtcMessagePart;
public sealed record Qnh(int Hpa) : AtcMessagePart;
/// <param name="Recycle">"recycle squawk 4521" (conformance level 2).</param>
public sealed record Squawk(int Code, bool Recycle = false) : AtcMessagePart;
public sealed record ReadbackCorrect : AtcMessagePart;

public enum ContactCondition
{
    None,
    WhenReadyForPushAndStart,
}

/// <summary>"contact Barcelona Tower 118.100". The unit carries the callsign, role and frequency.</summary>
public sealed record Contact(ControllerUnit Unit, ContactCondition Condition = ContactCondition.None) : AtcMessagePart;

/// <param name="Facing">Compass word the push ends facing ("south"); null when not planned.</param>
public sealed record PushStartApproved(string? Facing = null) : AtcMessagePart;

/// <summary>"taxi to holding point A1 runway 24L via K, M, hold short runway 02". Empty segments is the no-route fallback.</summary>
public sealed record TaxiVia(string? HoldingPoint, string Runway, IReadOnlyList<string> Segments, IReadOnlyList<string> HoldShorts) : AtcMessagePart;

public sealed record TaxiHoldingPoint(string Name) : AtcMessagePart;
public sealed record RunwayIs(string Runway) : AtcMessagePart;
public sealed record HoldShort(string Runway) : AtcMessagePart;
public sealed record LineUpAndWait(string Runway) : AtcMessagePart;
public sealed record HoldPosition : AtcMessagePart;

/// <param name="Emphasis">"stop immediately, I say again, stop" (takeoff without clearance).</param>
public sealed record StopImmediately(bool Emphasis = false) : AtcMessagePart;

/// <param name="Repeated">"go around, I say again, go around" (CAP 413).</param>
public sealed record GoAround(bool Repeated = false) : AtcMessagePart;

public sealed record FlyRunwayHeading : AtcMessagePart;

/// <summary>First radar unit on check-in.</summary>
public sealed record RadarContact : AtcMessagePart;

/// <summary>Later radar units on check-in.</summary>
public sealed record Identified : AtcMessagePart;

/// <summary>Reported altitude differs from Mode C by more than 200 ft.</summary>
public sealed record ConfirmAltitude : AtcMessagePart;

/// <param name="Type">Approach type, e.g. "ILS".</param>
public sealed record ExpectApproach(string Type, string Runway) : AtcMessagePart;

/// <param name="Via">STAR flown to the approach, when cleared together (design doc); null otherwise.</param>
public sealed record ClearedApproach(string Type, string Runway, string? Via = null) : AtcMessagePart;

/// <summary>"vectoring for ILS approach runway 24R" (CAP 413).</summary>
public sealed record VectoringFor(string Type, string Runway) : AtcMessagePart;

/// <summary>"closing localiser from the right" (CAP 413), with the final vector.</summary>
public sealed record ClosingLocaliser(bool FromRight) : AtcMessagePart;

/// <summary>"maintain 160 knots until 4 miles final" (CAP 413).</summary>
public sealed record SpeedUntil(int Knots, double MilesFinal) : AtcMessagePart;
public sealed record ReportEstablishedRequest : AtcMessagePart;
public sealed record VacateWhenAble : AtcMessagePart;

/// <summary>"taxi to stand 27 via M, A, hold short runway 18L"; a null stand is "taxi to parking".</summary>
public sealed record TaxiToStand(string? Stand, IReadOnlyList<string>? Segments = null, IReadOnlyList<string>? HoldShorts = null) : AtcMessagePart;

/// <summary>"cross runway 24" (ICAO / CAP 413), given when the aircraft reaches the hold-short.</summary>
public sealed record CrossRunway(string Runway) : AtcMessagePart;

/// <summary>The closing call when parked.</summary>
public sealed record ClosingCall : AtcMessagePart;

/// <summary>Surface wind in degrees magnetic, rounded to 10 degrees. Speed 0 means calm.</summary>
public sealed record Wind(int DirectionMagDeg, int SpeedKt) : AtcMessagePart;

/// <summary>"confirm information Charlie": the pilot reported an older ATIS letter (design doc "ATIS").</summary>
public sealed record ConfirmInformation(char Letter) : AtcMessagePart;

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
