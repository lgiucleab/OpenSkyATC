using OpenSkyAtc.Core.Airspace;
using OpenSkyAtc.Core.Model;

namespace OpenSkyAtc.Core.Brain;

/// <summary>
/// Airport data for the brain (design doc "Core interfaces"): SimConnect facilities in the app, JSON fixtures in
/// tests. Works for any airport the player's sim has, so nothing about airports ships with OpenSkyATC.
/// </summary>
public interface IAirportDataProvider
{
    /// <summary>The airport, or null when the sim does not know it.</summary>
    Task<AirportData?> LoadAsync(string icao);

    /// <summary>CENTER frequencies listed at airports within <paramref name="radiusNm"/> of the route points.</summary>
    Task<IReadOnlyList<CenterFrequency>> CenterFrequenciesAlongAsync(IReadOnlyList<Geo.GeoPoint> route, double radiusNm);
}
