using OpenSkyAtc.Core.Airspace;
using OpenSkyAtc.Core.Brain;
using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;

namespace OpenSkyAtc.Replay.Tests.Harness;

/// <summary>Reads <see cref="FacilityDump"/> JSON files from a folder (fixtures, or exports a player made).</summary>
public sealed class FacilityFileProvider(string folder) : IAirportDataProvider
{
    public async Task<AirportData?> LoadAsync(string icao)
    {
        string path = Path.Combine(folder, $"{icao.ToUpperInvariant()}.facility.json");
        if (!File.Exists(path)) return null;
        return FacilityConverter.ToAirportData(FacilityDump.Parse(await File.ReadAllTextAsync(path)));
    }

    public async Task<IReadOnlyList<CenterFrequency>> CenterFrequenciesAlongAsync(IReadOnlyList<GeoPoint> route, double radiusNm)
    {
        var result = new List<CenterFrequency>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.facility.json"))
        {
            var dump = FacilityDump.Parse(await File.ReadAllTextAsync(file));
            var at = new GeoPoint(dump.Latitude, dump.Longitude);
            if (route.Any(p => GeoMath.DistanceNm(p, at) <= radiusNm)) result.AddRange(FacilityConverter.CenterFrequencies(dump));
        }
        return result;
    }
}
