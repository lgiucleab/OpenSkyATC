using System.Runtime.CompilerServices;
using OpenSkyAtc.SimConnect;

namespace OpenSkyAtc.Recorder;

/// <summary>
/// Facility-data commands: export an airport to a JSON fixture, or survey which airports in a region list
/// CENTER frequencies. Kept separate from Main so no SimConnect type is touched before the DLLs are loaded.
/// </summary>
internal static class FacilityCommands
{
    private const int CenterType = 10;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Export(string icao, string? output)
    {
        using var exporter = new FacilityExporter();
        var dump = exporter.Export(icao, TimeSpan.FromSeconds(60));
        if (dump is null)
        {
            Console.Error.WriteLine($"No facility data for {icao} (unknown airport, or the sim did not answer).");
            return 1;
        }

        output ??= $"{dump.Icao}.facility.json";
        File.WriteAllText(output, dump.ToJson());
        Console.WriteLine($"{dump.Icao} {dump.Name} ({dump.City}): {dump.Runways.Count} runways, {dump.Frequencies.Count} frequencies, " +
                          $"{dump.TaxiPoints.Count} taxi points, {dump.TaxiPaths.Count} taxi paths, {dump.TaxiNames.Count} taxi names, " +
                          $"{dump.Parkings.Count} parking spots, {dump.Approaches.Count} approaches, {dump.Departures.Count} SIDs, {dump.Arrivals.Count} STARs");
        Console.WriteLine($"Written to {Path.GetFullPath(output)}");
        return 0;
    }

    /// <summary>Loads an airport live from the sim and prints a taxi route from a gate to every runway end.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Taxi(string icao)
    {
        using var provider = new SimConnectAirportProvider();
        var airport = provider.LoadAsync(icao).GetAwaiter().GetResult();
        if (airport is null)
        {
            Console.Error.WriteLine($"The sim has no airport {icao}.");
            return 1;
        }
        var planner = new Core.Planning.GraphTaxiPlanner();
        var gate = airport.Taxi?.Stands.FirstOrDefault(s => s.Gate) ?? airport.Taxi?.Stands.FirstOrDefault();
        Console.WriteLine($"{airport.Icao} {airport.Name}: {airport.Runways.Count} runways, {airport.Frequencies.Count} frequencies, TA {airport.TransitionAltitudeFt} ft, stands {airport.Taxi?.Stands.Count ?? 0}");
        foreach (var f in airport.Frequencies.GroupBy(f => f.Role).Select(g => g.First())) Console.WriteLine($"  {f.Callsign} {f.FrequencyKhz / 1000.0:0.000}");
        if (gate is null) return 0;
        foreach (var end in airport.Runways.SelectMany(r => new[] { r.First, r.Second }))
        {
            var plan = planner.PlanToHoldingPoint(airport, gate.Position, end);
            Console.WriteLine($"  stand {gate.Name} -> runway {end.Designator} ({end.ApproachType ?? "no approach"}): " + (plan.IsFallback
                ? $"taxi to holding point runway {end.Designator} (fallback: {plan.FallbackReason})"
                : $"via {string.Join(", ", plan.Via)}{string.Concat(plan.HoldShorts.Select(h => $", hold short runway {h}"))}"));
        }
        return 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int SurveyCenter(string icaoPrefix, int max, string? output)
    {
        using var exporter = new FacilityExporter();
        var airports = exporter.AllAirports(TimeSpan.FromSeconds(120))
            .Where(a => a.Icao.StartsWith(icaoPrefix, StringComparison.OrdinalIgnoreCase) && a.Icao.Length == 4)
            .OrderBy(a => a.Icao, StringComparer.Ordinal)
            .Take(max)
            .ToList();
        Console.WriteLine($"{airports.Count} airports starting with {icaoPrefix}; reading their frequencies...");

        var lines = new List<string> { "icao,name,frequencies,center_count,center_frequencies" };
        int withCenter = 0, done = 0;
        foreach (var a in airports)
        {
            var dump = exporter.Export(a.Icao, TimeSpan.FromSeconds(15), frequenciesOnly: true);
            done++;
            if (dump is null) continue;
            var centers = dump.Frequencies.Where(f => f.Type == CenterType).ToList();
            if (centers.Count > 0) withCenter++;
            string list = string.Join(" ", centers.Select(c => $"{c.FrequencyHz / 1e6:0.000}={c.Name}"));
            lines.Add($"{dump.Icao},\"{dump.Name}\",{dump.Frequencies.Count},{centers.Count},\"{list}\"");
            if (done % 25 == 0) Console.WriteLine($"  {done}/{airports.Count}, {withCenter} with CENTER so far");
        }

        output ??= $"center-survey-{icaoPrefix}.csv";
        File.WriteAllLines(output, lines);
        Console.WriteLine($"{withCenter} of {done} airports list at least one CENTER frequency. Written to {Path.GetFullPath(output)}");
        return 0;
    }
}
