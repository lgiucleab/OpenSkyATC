using OpenSkyAtc.Core.Model;

namespace OpenSkyAtc.Core.Conformance;

/// <summary>The checks from the design doc's conformance table.</summary>
public enum ConformanceCheck
{
    AltitudeBust,
    AltitudeNotComplying,
    Heading,
    OffRoute,
    Speed,
    Squawk,
    MissedHandoff,
    RunwayIncursion,
    TakeoffWithoutClearance,
    LandingWithoutClearance,
    TaxiOffRoute,
}

/// <summary>Tolerances and grace times. Values from the design doc unless noted; see docs/decisions.md (step 5).</summary>
public sealed record ConformanceOptions
{
    public double AltitudeBustFt { get; init; } = 300;
    public TimeSpan AltitudeBustGrace { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>No altitude checks for this long after a new altitude clearance.</summary>
    public TimeSpan AltitudeSuppression { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan NotComplyingGrace { get; init; } = TimeSpan.FromSeconds(60);
    public double ComplyingMovedFt { get; init; } = 300;                 // decision 5
    public double ComplyingVerticalSpeedFpm { get; init; } = 500;       // decision 5

    public double HeadingToleranceDeg { get; init; } = 20;
    public TimeSpan HeadingGrace { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan HeadingArmAfter { get; init; } = TimeSpan.FromSeconds(60);   // decision 4

    public double OffRouteNm { get; init; } = 3;
    public TimeSpan OffRouteGrace { get; init; } = TimeSpan.FromSeconds(30);
    public double InterceptDeg { get; init; } = 30;                      // decision 3

    public double SpeedLimitKt { get; init; } = 250;
    public double SpeedLimitBelowFt { get; init; } = 10_000;
    public double AssignedSpeedToleranceKt { get; init; } = 15;
    public TimeSpan SpeedGrace { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>An assigned speed is not checked for this long after it is given (time to slow down).</summary>
    public TimeSpan AssignedSpeedSuppression { get; init; } = TimeSpan.FromSeconds(60);

    public TimeSpan SquawkGrace { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan HandoffGrace { get; init; } = TimeSpan.FromSeconds(45);
    public int GuardKhz { get; init; } = 121_500;

    public double FinalMaxAglFt { get; init; } = 200;
    public double FinalMaxDistanceNm { get; init; } = 2;                 // decision 6
    public double FinalMaxOffsetDeg { get; init; } = 10;                 // decision 6

    public double TaxiOffRouteM { get; init; } = 50;
    public TimeSpan TaxiOffRouteGrace { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Minimum time between escalation levels (decision 1).</summary>
    public TimeSpan MinLevelInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Repeat interval for zero-grace (serious) checks (decision 1).</summary>
    public TimeSpan SeriousRepeat { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>A conformance warning was issued (level 1-2), or level 3 was reached (logged deviation / lost comms).</summary>
public sealed record ConformanceWarning(TimeSpan Time, ConformanceCheck Check, int Level, bool Serious) : LogEntry(Time);

/// <summary>
/// One episode of a check being out of tolerance, logged when it ends, for the debrief. Episodes the pilot
/// fixed within the grace time (level 0) are not logged unless serious.
/// </summary>
/// <param name="MaxLevel">Highest level reached (3 = logged deviation).</param>
/// <param name="Worst">Worst value seen, in <paramref name="Unit"/> (e.g. 650 "ft" past the cleared altitude).</param>
public sealed record ConformanceEpisode(
    TimeSpan Time, ConformanceCheck Check, TimeSpan Start, int MaxLevel, bool Serious, double Worst, string Unit) : LogEntry(Time)
{
    /// <summary>Counted as a deviation in the debrief (decision 2); otherwise a warning.</summary>
    public bool IsDeviation => Serious || MaxLevel >= 3;
}
