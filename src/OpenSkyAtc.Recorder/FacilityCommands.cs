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
