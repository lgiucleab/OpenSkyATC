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
    private FacilityExporter? _exporter;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyList<(string Icao, string Region, double Lat, double Lon)>? _allAirports;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>At most this many airports are read when looking for CENTER frequencies along a route.</summary>
    public int MaxAirportsForCenterSearch { get; init; } = 150;

    public async Task<AirportData?> LoadAsync(string icao)
    {
        var dump = await Locked(e => e.Export(icao, Timeout));
        return dump is null ? null : FacilityConverter.ToAirportData(dump);
    }

    /// <summary>Positions of route points from the sim's navdata; null for each point the sim does not know.</summary>
    /// <param name="points">Ident, region and kind ('W' waypoint, 'V' VOR, 'N' NDB).</param>
    public async Task<IReadOnlyList<GeoPoint?>> NavaidPositionsAsync(IReadOnlyList<(string Ident, string? Region, char Kind)> points) =>
        await Locked(e => (IReadOnlyList<GeoPoint?>)points.Select(p => e.NavaidPosition(p.Ident, p.Region, p.Kind, TimeSpan.FromSeconds(5))).ToList());

    public async Task<IReadOnlyList<CenterFrequency>> CenterFrequenciesAlongAsync(IReadOnlyList<GeoPoint> route, double radiusNm)
    {
        return await Locked(exporter =>
        {
            _allAirports ??= exporter.AllAirports(TimeSpan.FromMinutes(2));
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
                if (exporter.Export(icao, TimeSpan.FromSeconds(10), frequenciesOnly: true) is { } dump)
                    result.AddRange(FacilityConverter.CenterFrequencies(dump));
            }
            return (IReadOnlyList<CenterFrequency>)result;
        });
    }

    /// <summary>
    /// Runs <paramref name="work"/> on its own connection, opened on first use and opened again after the sim went
    /// away. Throws <see cref="System.Runtime.InteropServices.COMException"/> when the sim is not running.
    /// </summary>
    private async Task<T> Locked<T>(Func<FacilityExporter, T> work)
    {
        await _lock.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                for (int attempt = 0; ; attempt++)
                {
                    _exporter ??= new FacilityExporter("OpenSkyATC airport data");
                    try
                    {
                        if (!_exporter.IsClosed) return work(_exporter);
                    }
                    catch (System.Runtime.InteropServices.COMException) when (attempt == 0)
                    {
                    }
                    _exporter.Dispose();
                    _exporter = null;
                    if (attempt > 0) throw new System.Runtime.InteropServices.COMException("The sim closed the connection.");
                }
            });
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _exporter?.Dispose();
        _lock.Dispose();
    }
}
