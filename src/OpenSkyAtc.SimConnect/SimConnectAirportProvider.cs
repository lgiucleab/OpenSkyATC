using OpenSkyAtc.Core.Airspace;
using OpenSkyAtc.Core.Brain;
using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;

namespace OpenSkyAtc.SimConnect;

/// <summary>
/// Airport data straight from the player's running sim: any airport they have, default or add-on, with its taxi
/// network, frequencies and procedures. Nothing about airports ships with OpenSkyATC.
/// Call <see cref="SimConnectLibrary.EnsureLoaded"/> first.
/// </summary>
public sealed class SimConnectAirportProvider : IAirportDataProvider, IDisposable
{
    private readonly FacilityExporter _exporter = new("OpenSkyATC airport data");
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyList<(string Icao, string Region, double Lat, double Lon)>? _allAirports;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>At most this many airports are read when looking for CENTER frequencies along a route.</summary>
    public int MaxAirportsForCenterSearch { get; init; } = 150;

    public async Task<AirportData?> LoadAsync(string icao)
    {
        var dump = await Locked(() => _exporter.Export(icao, Timeout));
        return dump is null ? null : FacilityConverter.ToAirportData(dump);
    }

    public async Task<IReadOnlyList<CenterFrequency>> CenterFrequenciesAlongAsync(IReadOnlyList<GeoPoint> route, double radiusNm)
    {
        return await Locked(() =>
        {
            _allAirports ??= _exporter.AllAirports(TimeSpan.FromMinutes(2));
            var nearby = _allAirports
                .Where(a => a.Icao.Length == 4)
                .Select(a => (a.Icao, Nm: route.Min(p => GeoMath.DistanceNm(p, new GeoPoint(a.Lat, a.Lon)))))
                .Where(x => x.Nm <= radiusNm)
                .OrderBy(x => x.Nm)
                .Take(MaxAirportsForCenterSearch)
                .ToList();

            var result = new List<CenterFrequency>();
            foreach (var (icao, _) in nearby)
            {
                if (_exporter.Export(icao, TimeSpan.FromSeconds(10), frequenciesOnly: true) is { } dump)
                    result.AddRange(FacilityConverter.CenterFrequencies(dump));
            }
            return (IReadOnlyList<CenterFrequency>)result;
        });
    }

    private async Task<T> Locked<T>(Func<T> work)
    {
        await _lock.WaitAsync();
        try
        {
            return await Task.Run(work);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _exporter.Dispose();
        _lock.Dispose();
    }
}
