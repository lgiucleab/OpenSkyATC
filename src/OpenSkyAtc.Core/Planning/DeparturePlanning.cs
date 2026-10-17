using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Messages;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Random;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Core.Planning;

/// <summary>Preferred runways for an airport, from data/airport-sops.json (filled in by the project owner).</summary>
public sealed record AirportSop(string Icao, IReadOnlyList<string> PreferredRunways);

public sealed record SopConfig(IReadOnlyList<AirportSop> Airports)
{
    public static readonly SopConfig Empty = new([]);

    public IReadOnlyList<string> PreferredFor(string icao) =>
        Airports.FirstOrDefault(a => string.Equals(a.Icao, icao, StringComparison.OrdinalIgnoreCase))?.PreferredRunways ?? [];
}

public sealed record RunwayRules
{
    /// <summary>A preferred runway is kept with up to this much tailwind (design doc).</summary>
    public double MaxPreferredTailwindKt { get; init; } = 5;

    /// <summary>
    /// The flight plan's own runway is kept with up to this much tailwind (owner's checklist feedback: pilots plan
    /// with SimBrief's real-world runway; switching it at 5 kt felt wrong).
    /// </summary>
    public double MaxPlanTailwindKt { get; init; } = 10;

    /// <summary>Never pick a runway with more crosswind than this if another is available (design doc).</summary>
    public double MaxCrosswindKt { get; init; } = 25;

    /// <summary>The chosen runway stays locked unless the wind shifts by more than these (design doc).</summary>
    public double LockShiftDeg { get; init; } = 60;
    public double LockShiftKt { get; init; } = 10;
}

/// <summary>
/// Runway selection by wind (design doc "Runway selection"): highest headwind; a preferred runway (SOP file, or
/// the flight plan's runway) is kept with up to 5 kt of tailwind; never more than 25 kt of crosswind if another
/// runway is available.
/// </summary>
public static class RunwaySelector
{
    public static double HeadwindKt(RunwayEnd end, double windFromTrueDeg, double windKt) =>
        windKt * Math.Cos((windFromTrueDeg - end.HeadingTrueDeg) * Math.PI / 180);

    public static double CrosswindKt(RunwayEnd end, double windFromTrueDeg, double windKt) =>
        Math.Abs(windKt * Math.Sin((windFromTrueDeg - end.HeadingTrueDeg) * Math.PI / 180));

    public static RunwayEnd? Select(AirportData airport, double windFromTrueDeg, double windKt, string? hint,
        IReadOnlyList<string>? preferred = null, RunwayRules? rules = null)
    {
        rules ??= new RunwayRules();
        var ends = airport.Runways.SelectMany(r => new[] { r.First, r.Second }).ToList();
        if (ends.Count == 0) return null;

        var withinCrosswind = ends.Where(e => CrosswindKt(e, windFromTrueDeg, windKt) <= rules.MaxCrosswindKt).ToList();
        var pool = withinCrosswind.Count > 0 ? withinCrosswind : ends;

        var wanted = new HashSet<string>(preferred ?? [], StringComparer.OrdinalIgnoreCase);

        IEnumerable<RunwayEnd> Best(IEnumerable<RunwayEnd> xs) => xs
            .OrderByDescending(e => Math.Round(HeadwindKt(e, windFromTrueDeg, windKt), 6))
            .ThenBy(e => e.Designator, StringComparer.Ordinal);

        // The plan's runway first, then the airport's preferred ones, then the best for the wind.
        if (hint is not null && pool.FirstOrDefault(e => e.Designator.Equals(hint, StringComparison.OrdinalIgnoreCase)) is { } planned
            && HeadwindKt(planned, windFromTrueDeg, windKt) >= -rules.MaxPlanTailwindKt)
            return planned;
        var preferredOk = pool.Where(e => wanted.Contains(e.Designator) && HeadwindKt(e, windFromTrueDeg, windKt) >= -rules.MaxPreferredTailwindKt);
        return Best(preferredOk).FirstOrDefault() ?? Best(pool).First();
    }
}

/// <summary>Keeps a runway choice for the session unless the wind shifts by more than 60 degrees or 10 kt.</summary>
public sealed class RunwayLock(RunwayRules? rules = null)
{
    private readonly RunwayRules _rules = rules ?? new RunwayRules();
    private (double Dir, double Kt)? _wind;

    public RunwayEnd? Runway { get; private set; }

    public RunwayEnd? Choose(AirportData airport, double windFromTrueDeg, double windKt, string? hint, IReadOnlyList<string> preferred)
    {
        if (Runway is not null && _wind is { } w && !Shifted(w, windFromTrueDeg, windKt)) return Runway;
        Runway = RunwaySelector.Select(airport, windFromTrueDeg, windKt, hint, preferred, _rules);
        _wind = (windFromTrueDeg, windKt);
        return Runway;
    }

    private bool Shifted((double Dir, double Kt) was, double dir, double kt)
    {
        if (Math.Abs(kt - was.Kt) > _rules.LockShiftKt) return true;
        if (kt < 3 && was.Kt < 3) return false; // direction means nothing in calm wind
        return Math.Abs(GeoMath.AngleDiffDeg(dir, was.Dir)) > _rules.LockShiftDeg;
    }
}

/// <summary>A configured squawk range: codes <see cref="First"/> to <see cref="Last"/> (octal digits, inclusive) for airports whose ICAO starts with <see cref="IcaoPrefix"/>.</summary>
public sealed record SquawkRange(string IcaoPrefix, int First, int Last);

/// <summary>Per-region squawk ranges, loaded from data/squawk-ranges.json by the app.</summary>
public sealed record SquawkConfig(IReadOnlyList<SquawkRange> Ranges)
{
    public static readonly SquawkConfig Empty = new([]);
}

public static class SquawkAllocator
{
    /// <summary>
    /// Never assigned. The first five are from the design doc; 7000 (European VFR conspicuity) and 0000
    /// are added because an IFR flight must not get them either.
    /// </summary>
    public static readonly IReadOnlySet<int> Reserved = new HashSet<int> { 7500, 7600, 7700, 2000, 1000, 7000, 0 };

    /// <summary>
    /// Picks a code from the longest-prefix range for the origin; with no matching range, from every valid
    /// code that is not reserved.
    /// </summary>
    public static int Allocate(SquawkConfig config, string originIcao, IRandom rng)
    {
        var range = config.Ranges
            .Where(r => originIcao.StartsWith(r.IcaoPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.IcaoPrefix.Length)
            .FirstOrDefault();

        var codes = (range is null ? AllCodes() : AllCodes().Where(c => c >= range.First && c <= range.Last))
            .Where(c => !Reserved.Contains(c))
            .ToArray();
        if (codes.Length == 0) throw new InvalidOperationException($"Squawk range for '{range?.IcaoPrefix}' has no usable codes.");
        return codes[rng.Next(0, codes.Length)];
    }

    public static bool IsValidCode(int code) => code is >= 0 and <= 7777 && code.ToString().All(ch => ch <= '7');

    private static IEnumerable<int> AllCodes() => Enumerable.Range(0, 7778).Where(IsValidCode);
}

/// <summary>
/// The SID to clear: the plan's when the sim has it for the assigned runway; otherwise the sim's SID with the same
/// name stem for that runway (the plan's AIRAC can be newer than the sim's navdata: PINA3N planned, PINA4N in the
/// sim), preferring the same final letter; otherwise none ("flight planned route"). Without SID data in the sim, the
/// plan's SID is used as filed.
/// </summary>
public static class SidChooser
{
    public static string? Choose(AirportData origin, string? plannedSid, RunwayEnd? runway)
    {
        if (plannedSid is null) return null;
        if (origin.Sids.Count == 0) return plannedSid;
        bool Fits(SidInfo s) => runway is null || s.Serves(runway.Designator);

        if (origin.Sids.FirstOrDefault(s => s.Name.Equals(plannedSid, StringComparison.OrdinalIgnoreCase)) is { } exact && Fits(exact)) return exact.Name;

        string stem = Stem(plannedSid);
        char last = char.ToUpperInvariant(plannedSid[^1]);
        return origin.Sids
            .Where(s => stem.Length > 0 && Stem(s.Name).Equals(stem, StringComparison.OrdinalIgnoreCase) && Fits(s))
            .OrderByDescending(s => char.ToUpperInvariant(s.Name[^1]) == last)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .Select(s => s.Name)
            .FirstOrDefault();
    }

    /// <summary>The fix part of a SID name: "PINA3N" -> "PINA", "CCS2X" -> "CCS".</summary>
    private static string Stem(string sid) => new(sid.TakeWhile(char.IsLetter).ToArray());
}

public static class InitialClimbRule
{
    public const int DefaultFeet = 5000;
    public const int HighTransitionLevel = 70;

    /// <summary>
    /// Design doc: the SID's top altitude when the sim has it; otherwise 5,000 ft, or FL070 above a high
    /// transition altitude (read as: when the airport's TA is known and 5,000 ft would be at or above it).
    /// </summary>
    public static AltitudeTarget For(AirportData origin, string? sid = null)
    {
        if (sid is not null && origin.Sids.FirstOrDefault(s => string.Equals(s.Name, sid, StringComparison.OrdinalIgnoreCase))?.TopAltitudeFt is { } top)
            return origin.TransitionAltitudeFt is { } t && top >= t ? AltitudeTarget.FlightLevel(top / 100) : AltitudeTarget.Feet(top);
        return origin.TransitionAltitudeFt is { } ta && ta <= DefaultFeet
            ? AltitudeTarget.FlightLevel(HighTransitionLevel)
            : AltitudeTarget.Feet(DefaultFeet);
    }
}

public static class WindReport
{
    /// <summary>
    /// Surface wind for a clearance: magnetic (variation from the aircraft's true and magnetic heading),
    /// rounded to 10 degrees with 360 for north, speed rounded to the knot; under 1 kt is calm.
    /// </summary>
    public static Wind From(SimState s)
    {
        int speed = (int)Math.Round(s.WindSpeedKt);
        if (speed < 1) return new Wind(0, 0);

        double variation = GeoMath.AngleDiffDeg(s.HeadingTrueDeg, s.HeadingMagneticDeg);
        int dir = (int)(Math.Round(GeoMath.NormalizeDeg(s.WindDirectionTrueDeg - variation) / 10) * 10);
        return new Wind(dir is 0 or 360 ? 360 : dir, speed);
    }
}
