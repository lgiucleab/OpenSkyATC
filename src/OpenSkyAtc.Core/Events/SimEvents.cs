using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Core.Events;

/// <summary>A named event derived from consecutive <see cref="SimState"/> snapshots. Time is the sim time of the tick that fired it.</summary>
public abstract record SimEvent(TimeSpan Time);

/// <summary>An engine's running flag turned on and stayed on for the hold time.</summary>
public sealed record EnginesStarted(TimeSpan Time, int EngineIndex, int RunningCount) : SimEvent(Time);

/// <summary>An engine's running flag turned off and stayed off for the hold time.</summary>
public sealed record EnginesStopped(TimeSpan Time, int EngineIndex, int RunningCount) : SimEvent(Time);

public sealed record TaxiStarted(TimeSpan Time) : SimEvent(Time);

public sealed record EnteredRunway(TimeSpan Time, Runway Runway) : SimEvent(Time);
public sealed record ExitedRunway(TimeSpan Time, Runway Runway) : SimEvent(Time);

public sealed record PassedHoldShort(TimeSpan Time, Runway Runway, HoldShortPoint HoldShort) : SimEvent(Time);

public sealed record TakeoffRoll(TimeSpan Time, Runway Runway) : SimEvent(Time);

/// <param name="LiftOff">When the wheels left the ground (start of the hold period).</param>
public sealed record Airborne(TimeSpan Time, TimeSpan LiftOff) : SimEvent(Time);

/// <param name="FirstContact">When the wheels touched (start of the hold period).</param>
public sealed record Touchdown(TimeSpan Time, TimeSpan FirstContact) : SimEvent(Time);

public sealed record AltitudeCrossed(TimeSpan Time, AltitudeRegistration Registration, bool Climbing) : SimEvent(Time);

public sealed record LevelOff(TimeSpan Time, double AltitudeFt) : SimEvent(Time);

/// <param name="Inbound">True when the distance fell below the registered value, false when it rose above it.</param>
public sealed record DistanceCrossed(TimeSpan Time, DistanceRegistration Registration, bool Inbound) : SimEvent(Time);

/// <param name="Com">1 or 2.</param>
public sealed record FrequencyChanged(TimeSpan Time, int Com, int FrequencyKhz) : SimEvent(Time);

public sealed record SquawkChanged(TimeSpan Time, int Code) : SimEvent(Time);
public sealed record TransponderModeChanged(TimeSpan Time, TransponderMode Mode) : SimEvent(Time);

public sealed record Established(TimeSpan Time, EstablishedRegistration Registration) : SimEvent(Time);

public sealed record SimPaused(TimeSpan Time) : SimEvent(Time);
public sealed record SimResumed(TimeSpan Time) : SimEvent(Time);

public enum TeleportReason
{
    /// <summary>The position jumped more than the teleport distance in one tick.</summary>
    Jump,

    /// <summary>Slew mode ended.</summary>
    Slew,
}

/// <summary>A teleport, slew or flight restart. No other events fire until <see cref="PositionSettled"/>.</summary>
public sealed record Teleported(TimeSpan Time, double DistanceNm, TeleportReason Reason) : SimEvent(Time);

/// <summary>The aircraft has been stable for the settle time after a teleport; detectors start again from a fresh baseline.</summary>
public sealed record PositionSettled(TimeSpan Time) : SimEvent(Time);
