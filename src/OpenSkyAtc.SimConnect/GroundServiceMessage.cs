namespace OpenSkyAtc.SimConnect;

/// <summary>
/// GROUND_SERVICE_ATC_MESSAGE values for the pushback, checked in MSFS 2024: each one changes PUSHBACK STATE
/// (8 starts it, 14 straight, 12 left, 13 right, 9 stops it).
/// </summary>
public enum GroundServiceMessage : uint
{
    RequestPushback = 8,
    StopPushback = 9,
    PushbackSteerLeft = 12,
    PushbackSteerRight = 13,
    PushbackStraighten = 14,
}
