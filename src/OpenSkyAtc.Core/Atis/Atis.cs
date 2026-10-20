using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Messages;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Planning;
using OpenSkyAtc.Core.Random;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Core.Atis;

public sealed record AtisOptions
{
    /// <summary>
    /// The sim reports weather only where the aircraft is, so an airport's ATIS broadcasts only within this distance
    /// (owner's choice); further out, tuning it gives silence, like being out of range.
    /// </summary>
    public double RangeNm { get; init; } = 60;
}

/// <summary>
/// One ATIS broadcast (design doc "ATIS"), in ICAO Annex 11 order: the airport's information letter and observation
/// time, the expected approach (arrival only) and runway in use, transition level, surface wind, visibility,
/// temperature and QNH. MSFS gives neither dewpoint nor cloud, so both are left out (owner's choice).
/// </summary>
/// <param name="Unit">The ATIS unit ("Barajas Information").</param>
/// <param name="TimeUtc">Observation time: when this letter was made.</param>
/// <param name="ApproachType">"ILS" for the destination's ATIS; null for the departure airport.</param>
public sealed record AtisReport(
    ControllerUnit Unit, string AirportIcao, char Letter, DateTime TimeUtc, string? Runway, string? ApproachType,
    int? TransitionLevel, Wind Wind, double VisibilityM, int TemperatureC, int Qnh);

/// <summary>
/// An airport's ATIS: keeps the current letter and report, and moves to the next letter (A to Z, then A) when the
/// runway in use changes, QNH changes by 1 hPa or more, or the wind shifts enough to re-score the runways (the
/// runway lock's thresholds). Updated every tick from the sim's weather at the aircraft, so a flight log replays it
/// the same.
/// </summary>
public sealed class AtisStation
{
    private readonly RunwayRules _rules;
    private (string? Runway, int Qnh, double WindDir, double WindKt)? _basis;

    public AtisStation(ControllerUnit unit, AirportData airport, bool arrival, IRandom rng, RunwayRules? rules = null)
    {
        Unit = unit;
        Airport = airport;
        IsArrival = arrival;
        _rules = rules ?? new RunwayRules();
        Letter = (char)('A' + rng.Next(0, 26));
    }

    public ControllerUnit Unit { get; }
    public AirportData Airport { get; }
    public bool IsArrival { get; }
    public char Letter { get; private set; }

    /// <summary>The broadcast now; null before the first update in range.</summary>
    public AtisReport? Current { get; private set; }

    /// <summary>True when the last update made a new letter (for the log).</summary>
    public bool Changed { get; private set; }

    public void Update(SimState s, RunwayEnd? runway)
    {
        Changed = false;
        int qnh = FlightPlanning.Qnh(s);
        var basis = (runway?.Designator, qnh, s.WindDirectionTrueDeg, s.WindSpeedKt);
        if (_basis is { } was)
        {
            if (!Differs(was, basis)) return;
            Letter = Letter == 'Z' ? 'A' : (char)(Letter + 1);
            Changed = true;
        }
        _basis = basis;
        Current = new AtisReport(Unit, Airport.Icao, Letter, s.ZuluTime, runway?.Designator,
            IsArrival && runway is not null ? runway.ApproachType ?? "ILS" : null,
            Airport.TransitionLevel, WindReport.From(s), s.VisibilityM, (int)Math.Round(s.TemperatureC), qnh);
    }

    private bool Differs((string? Runway, int Qnh, double WindDir, double WindKt) was, (string? Runway, int Qnh, double WindDir, double WindKt) now)
    {
        if (was.Runway != now.Runway || Math.Abs(was.Qnh - now.Qnh) >= 1) return true;
        if (Math.Abs(now.WindKt - was.WindKt) > _rules.LockShiftKt) return true;
        if (now.WindKt < 3 && was.WindKt < 3) return false; // direction means nothing in calm wind
        return Math.Abs(GeoMath.AngleDiffDeg(now.WindDir, was.WindDir)) > _rules.LockShiftDeg;
    }
}

/// <summary>The flight's ATIS stations (origin and destination, where the sim has an ATIS frequency).</summary>
public sealed class AtisService
{
    private readonly List<AtisStation> _stations = [];
    private readonly AtisOptions _o;

    public AtisService(ControllerUnit? originAtis, AirportData origin, ControllerUnit? destinationAtis, AirportData destination,
        IRandom rng, AtisOptions? options = null, RunwayRules? rules = null)
    {
        _o = options ?? new AtisOptions();
        if (originAtis is not null) _stations.Add(new AtisStation(originAtis, origin, arrival: false, rng, rules));
        if (destinationAtis is not null && destinationAtis != originAtis) _stations.Add(new AtisStation(destinationAtis, destination, arrival: true, rng, rules));
    }

    public IReadOnlyList<AtisStation> Stations => _stations;

    public AtisStation? Origin => _stations.FirstOrDefault(s => !s.IsArrival);

    /// <summary>Updates every station in range; returns the ones that made a new letter.</summary>
    public IReadOnlyList<AtisStation> Update(SimState s, Func<AtisStation, RunwayEnd?> runwayInUse)
    {
        var changed = new List<AtisStation>();
        foreach (var station in _stations)
        {
            if (!InRange(station, s)) continue;
            station.Update(s, runwayInUse(station));
            if (station.Changed) changed.Add(station);
        }
        return changed;
    }

    public bool InRange(AtisStation station, SimState s) =>
        station.Airport.ReferencePoint is { } p && GeoMath.DistanceNm(p, new GeoPoint(s.Latitude, s.Longitude)) <= _o.RangeNm;

    /// <summary>The broadcast on a frequency the aircraft has tuned, or null (no ATIS there, or out of range).</summary>
    public AtisReport? On(int khz, SimState s) =>
        _stations.FirstOrDefault(st => st.Unit.Answers(khz) && InRange(st, s))?.Current;
}
