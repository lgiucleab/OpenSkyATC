using System.Text.Json;
using OpenSkyAtc.Core.Airspace;
using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Planning;

namespace OpenSkyAtc.Core.Config;

/// <summary>
/// Parses the owner-filled files in data/ (the host reads them; Core only sees the text). Every file is optional
/// data: an empty list means "no data", never a guess. A malformed file throws <see cref="FormatException"/>.
/// </summary>
public static class DataFiles
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed record SquawkFile(List<SquawkEntry>? Ranges);
    private sealed record SquawkEntry(string IcaoPrefix, string First, string Last);
    private sealed record SopFile(List<AirportSop>? Airports);
    private sealed record CtrFile(List<CtrEntry>? Sectors);
    private sealed record CtrEntry(string Name, double FrequencyMhz, double Latitude, double Longitude);

    /// <summary>data/squawk-ranges.json: { "ranges": [ { "icaoPrefix": "LE", "first": "3401", "last": "3477" } ] }.</summary>
    public static SquawkConfig ParseSquawks(string json)
    {
        var file = Read<SquawkFile>(json, "squawk-ranges.json");
        return new SquawkConfig((file.Ranges ?? []).Select(r => new SquawkRange(r.IcaoPrefix, Octal(r.First), Octal(r.Last))).ToList());
    }

    /// <summary>data/airport-sops.json: { "airports": [ { "icao": "LEBL", "preferredRunways": ["24L", "24R"] } ] }.</summary>
    public static SopConfig ParseSops(string json) => new(Read<SopFile>(json, "airport-sops.json").Airports ?? []);

    /// <summary>
    /// data/ctr-frequencies.json: { "sectors": [ { "name": "Madrid Control", "frequencyMhz": 133.1, "latitude": 40, "longitude": -3.7 } ] }.
    /// Each becomes a CENTER frequency at that position, the same as one from the sim's airport data.
    /// </summary>
    public static IReadOnlyList<CenterFrequency> ParseCtrSectors(string json) =>
        (Read<CtrFile>(json, "ctr-frequencies.json").Sectors ?? [])
        .Select(s =>
        {
            if (s.FrequencyMhz is < 118 or > 137) throw new FormatException($"ctr-frequencies.json: {s.Name} has frequency {s.FrequencyMhz}, outside 118-137 MHz.");
            return new CenterFrequency((int)Math.Round(s.FrequencyMhz * 1000), new GeoPoint(s.Latitude, s.Longitude), "", s.Name);
        })
        .ToList();

    private static T Read<T>(string json, string file)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options) ?? throw new FormatException($"{file} is empty.");
        }
        catch (JsonException e)
        {
            throw new FormatException($"{file}: {e.Message}", e);
        }
    }

    /// <summary>"3401" -> 3401, after checking it is four octal digits.</summary>
    private static int Octal(string code)
    {
        if (code.Length != 4 || code.Any(c => c is < '0' or > '7')) throw new FormatException($"squawk-ranges.json: '{code}' is not a four-digit octal code.");
        return int.Parse(code, System.Globalization.CultureInfo.InvariantCulture);
    }
}
