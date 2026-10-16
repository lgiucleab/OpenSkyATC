namespace OpenSkyAtc.Core.Events;

/// <summary>
/// Thresholds, hold times and hysteresis for every event. Values marked "doc" come from the design doc;
/// the others are starting values chosen in step 2 (see docs/decisions.md) and are meant to be tuned.
/// </summary>
public sealed record EventEngineOptions
{
    public TimeSpan EngineHold { get; init; } = TimeSpan.FromSeconds(2);              // doc

    public double TaxiStartKt { get; init; } = 3;                                      // doc
    public TimeSpan TaxiStartHold { get; init; } = TimeSpan.FromSeconds(3);           // doc
    public double TaxiRearmKt { get; init; } = 1;
    public TimeSpan TaxiRearmHold { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan AirborneHold { get; init; } = TimeSpan.FromSeconds(3);            // doc
    public TimeSpan AirborneRearm { get; init; } = TimeSpan.FromSeconds(5);           // doc
    public TimeSpan TouchdownHold { get; init; } = TimeSpan.FromSeconds(3);           // doc
    public TimeSpan TouchdownRearm { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Distance outside the runway polygon before the aircraft counts as having left it.</summary>
    public double RunwayMarginM { get; init; } = 5;

    /// <summary>Distance either side of a hold-short line before the side counts as changed.</summary>
    public double HoldShortMarginM { get; init; } = 2;

    /// <summary>How far sideways from the hold-short point the line extends.</summary>
    public double HoldShortHalfWidthM { get; init; } = 30;

    public double TakeoffRollKt { get; init; } = 40;                                   // doc
    public double TakeoffRollRearmKt { get; init; } = 30;
    public TimeSpan TakeoffRollRearmHold { get; init; } = TimeSpan.FromSeconds(5);

    public double AltitudeBandFt { get; init; } = 50;

    public double LevelOffFpm { get; init; } = 200;                                    // doc
    public TimeSpan LevelOffHold { get; init; } = TimeSpan.FromSeconds(10);           // doc
    public double LevelOffRearmFpm { get; init; } = 500;
    public TimeSpan LevelOffRearmHold { get; init; } = TimeSpan.FromSeconds(5);

    public double DistanceBandNm { get; init; } = 0.1;

    public TimeSpan FrequencyHold { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan TransponderHold { get; init; } = TimeSpan.FromSeconds(2);         // doc

    public double EstablishedDeg { get; init; } = 1;                                   // doc
    public double EstablishedReleaseDeg { get; init; } = 2;
    public TimeSpan EstablishedHold { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan EstablishedRearmHold { get; init; } = TimeSpan.FromSeconds(5);

    public double TeleportNm { get; init; } = 2;                                       // doc
    public TimeSpan SettleTime { get; init; } = TimeSpan.FromSeconds(10);             // doc
}
