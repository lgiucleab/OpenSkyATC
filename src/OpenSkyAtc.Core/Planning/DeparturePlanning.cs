using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Messages;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Random;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Core.Planning;

/// <summary>
/// Runway choice for step 3: the plan's runway hint when it exists, otherwise the highest headwind.
/// Step 7 adds the tailwind, preferred-runway, crosswind and runway-lock rules.
/// </summary>
public static class RunwaySelector
{
    public static double HeadwindKt(RunwayEnd end, double windFromTrueDeg, double windKt) =>
        windKt * Math.Cos((windFromTrueDeg - end.HeadingTrueDeg) * Math.PI / 180);

    public static RunwayEnd? Select(AirportData airport, double windFromTrueDeg, double windKt, string? hint)
    {
        if (hint is not null && airport.FindRunway(hint)?.End(hint) is { } hinted) return hinted;

        return airport.Runways
            .SelectMany(r => new[] { r.First, r.Second })
            .OrderByDescending(e => HeadwindKt(e, windFromTrueDeg, windKt))
            .FirstOrDefault();
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

public static class InitialClimbRule
{
    public const int DefaultFeet = 5000;
    public const int HighTransitionLevel = 70;

    /// <summary>
    /// Design doc: 5,000 ft, or FL070 above a high transition altitude. Read as: when the airport's
    /// transition altitude is known and 5,000 ft would be at or above it, clear FL070 instead.
    /// The SID top altitude takes precedence once SID data exists.
    /// </summary>
    public static AltitudeTarget For(AirportData origin) =>
        origin.TransitionAltitudeFt is { } ta && ta <= DefaultFeet
            ? AltitudeTarget.FlightLevel(HighTransitionLevel)
            : AltitudeTarget.Feet(DefaultFeet);
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
