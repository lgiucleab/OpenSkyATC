using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Random;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Core.Planning;

public sealed record PlanningOptions
{
    /// <summary>Wait after the trigger (level-off or 2,000 ft below the cleared level) before the next climb step.</summary>
    public (double Min, double Max) StepClimbDelaySeconds { get; init; } = (30, 120);

    /// <summary>The climb to cruise is split into this many steps.</summary>
    public (int Min, int Max) StepClimbCount { get; init; } = (2, 6);

    /// <summary>Share of steps held down "for traffic" (silently delayed).</summary>
    public double TrafficHoldDownProbability { get; init; } = 0.2;

    public (double Min, double Max) TrafficHoldDownSeconds { get; init; } = (60, 120);

    /// <summary>Random addition to the top-of-descent distance (design doc: 0-15 nm).</summary>
    public (double Min, double Max) TodExtraNm { get; init; } = (0, 15);

    /// <summary>A pilot descent request within this distance of the descent point is granted.</summary>
    public double DescentRequestWindowNm { get; init; } = 30;

    /// <summary>CTR descends to this level first (decision B2), or straight to the approach altitude when cruise is lower.</summary>
    public int DescentFlightLevel { get; init; } = 100;

    /// <summary>Without procedure data, the approach altitude is field elevation plus this, rounded up to 500 ft (decision B2).</summary>
    public int ApproachHeightAboveFieldFt { get; init; } = 3000;

    /// <summary>
    /// Used only when an airport's transition altitude is unknown, to decide between feet and flight levels.
    /// Not from a source: a placeholder until the transition-altitude data file is filled in.
    /// </summary>
    public int DefaultTransitionAltitudeFt { get; init; } = 10_000;
}

/// <summary>
/// The controller's vertical plan for the flight: step climbs to cruise, the top of descent and the arrival
/// levels. Built once per session; the en route and arrival phases read and advance it.
/// </summary>
public sealed class FlightPlanning
{
    private readonly PlanningOptions _o;
    private readonly IRandom _rng;
    private readonly AirportData _origin;
    private readonly AirportData _destination;
    private readonly double _todExtraNm;
    private TimeSpan? _stepDue;

    public FlightPlanning(FlightPlan plan, AirportData origin, AirportData destination, IRandom rng, PlanningOptions? options = null)
    {
        _o = options ?? new PlanningOptions();
        _rng = rng;
        _origin = origin;
        _destination = destination;

        var initial = InitialClimbRule.For(origin);
        Cruise = plan.CruiseLevel is { } c && c.NominalFeet > initial.NominalFeet ? c : initial;
        ClimbSteps = PlanSteps(initial.NominalFeet, Cruise, rng.Next(_o.StepClimbCount.Min, _o.StepClimbCount.Max + 1));
        _todExtraNm = Math.Round(_o.TodExtraNm.Min + rng.NextDouble() * (_o.TodExtraNm.Max - _o.TodExtraNm.Min), 1);
        ArrivalRunwayHint = plan.ArrivalRunway;
    }

    public AltitudeTarget Cruise { get; }

    /// <summary>Levels the aircraft is cleared to after the initial climb, ending at cruise.</summary>
    public IReadOnlyList<AltitudeTarget> ClimbSteps { get; }

    /// <summary>True while a step is being held down "for traffic".</summary>
    public bool HoldingDown { get; private set; }

    public string? ArrivalRunwayHint { get; }

    /// <summary>A go-around happened and the missed approach is being flown; Approach gives the new approach once level.</summary>
    public bool MissedApproachInProgress { get; set; }
    public RunwayEnd? ArrivalRunway { get; private set; }

    /// <summary>The next climb step above the current cleared level, or null at cruise.</summary>
    public AltitudeTarget? NextClimbStep(AltitudeTarget? cleared) =>
        ClimbSteps.FirstOrDefault(s => s.NominalFeet > (cleared?.NominalFeet ?? 0)) is { Value: > 0 } next ? next : null;

    /// <summary>Starts the wait for the next step (after a level-off or passing 2,000 ft below the cleared level).</summary>
    public void ScheduleStep(TimeSpan now)
    {
        if (_stepDue is not null) return;
        double wait = Sample(_o.StepClimbDelaySeconds);
        HoldingDown = _rng.NextDouble() < _o.TrafficHoldDownProbability;
        if (HoldingDown) wait += Sample(_o.TrafficHoldDownSeconds);
        _stepDue = now + TimeSpan.FromSeconds(Math.Round(wait, 1));
    }

    public bool StepDue(TimeSpan now) => _stepDue is { } due && due <= now;

    /// <summary>Call when a step has been issued.</summary>
    public void StepIssued()
    {
        _stepDue = null;
        HoldingDown = false;
    }

    /// <summary>Design doc 3:1 rule: (cruise ft - target ft) / 1000 x 3 + 10 nm, before the random addition.</summary>
    public static double TodNm(int cruiseFt, int targetFt) => Math.Max(0, cruiseFt - targetFt) / 1000.0 * 3 + 10;

    /// <summary>Distance from the destination at which the descent is issued: the TOD plus a random 0-15 nm.</summary>
    public double DescentPointNm => TodNm(Cruise.NominalFeet, ApproachAltitudeFt()) + _todExtraNm;

    public bool DescentDue(double remainingNm) => remainingNm <= DescentPointNm;

    public bool DescentRequestGranted(double remainingNm) => remainingNm <= DescentPointNm + _o.DescentRequestWindowNm;

    /// <summary>First descent level (decision B2).</summary>
    public AltitudeTarget DescentLevel =>
        Cruise.NominalFeet > _o.DescentFlightLevel * 100 ? AltitudeTarget.FlightLevel(_o.DescentFlightLevel) : ApproachAltitude();

    /// <summary>The arrival runway end, chosen once from the wind at the aircraft.</summary>
    public RunwayEnd? ChooseArrivalRunway(SimState s) =>
        ArrivalRunway ??= RunwaySelector.Select(_destination, s.WindDirectionTrueDeg, s.WindSpeedKt, ArrivalRunwayHint);

    /// <summary>Procedure altitude when known, else field elevation + 3,000 ft rounded up to 500 ft (decision B2).</summary>
    public int ApproachAltitudeFt()
    {
        if (ArrivalRunway?.ApproachAltitudeFt is { } procedure) return procedure;
        double field = _destination.ElevationFt ?? 0;
        return (int)(Math.Ceiling((field + _o.ApproachHeightAboveFieldFt) / 500) * 500);
    }

    public AltitudeTarget ApproachAltitude() => ToTarget(ApproachAltitudeFt(), _destination);

    /// <summary>QNH from the sea-level pressure at the aircraft (decision B3), rounded down to the hPa.</summary>
    public static int Qnh(SimState s) => (int)Math.Floor(s.SeaLevelPressureHpa);

    /// <summary>Feet at or above the airport's transition altitude become a flight level.</summary>
    public AltitudeTarget ToTarget(int feet, AirportData airport)
    {
        int ta = airport.TransitionAltitudeFt ?? _o.DefaultTransitionAltitudeFt;
        return feet >= ta ? AltitudeTarget.FlightLevel(feet / 100) : AltitudeTarget.Feet(feet);
    }

    /// <summary>Current altitude rounded to the nearest 1,000 ft, as a target (for sessions that start in the air).</summary>
    public AltitudeTarget CurrentLevel(double altitudeFt, AirportData nearest) =>
        ToTarget((int)(Math.Round(altitudeFt / 1000) * 1000), nearest);

    private List<AltitudeTarget> PlanSteps(int initialFt, AltitudeTarget cruise, int count)
    {
        var steps = new List<AltitudeTarget>();
        int cruiseFt = cruise.NominalFeet;
        for (int k = 1; k < count; k++)
        {
            int ft = (int)(Math.Round((initialFt + (cruiseFt - initialFt) * (double)k / count) / 1000) * 1000);
            if (ft <= initialFt || ft >= cruiseFt || steps.Any(s => s.NominalFeet == ft)) continue;
            steps.Add(ToTarget(ft, _origin));
        }
        if (cruiseFt > initialFt) steps.Add(cruise);
        return steps;
    }

    private double Sample((double Min, double Max) r) => r.Min + _rng.NextDouble() * (r.Max - r.Min);
}
