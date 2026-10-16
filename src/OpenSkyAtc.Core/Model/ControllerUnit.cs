using OpenSkyAtc.Core.Geo;

namespace OpenSkyAtc.Core.Model;

public enum ControllerRole
{
    Atis,
    Delivery,
    Ground,
    Tower,
    Departure,
    Approach,
    Center,
}

/// <summary>A volume of airspace a unit owns. Step 6 adds the generated volumes.</summary>
public interface IAirspaceVolume
{
    bool Contains(GeoPoint position, double altitudeFt);
}

/// <summary>A controller unit, e.g. "Barcelona Ground" on 121.700.</summary>
/// <param name="FrequencyKhz">The frequency ATC gives in handoffs.</param>
/// <param name="OtherFrequenciesKhz">
/// The airport's other frequencies for the same role (the sim often lists several Ground or Delivery frequencies):
/// the unit also answers on them, so a pilot who tunes any published one is heard.
/// </param>
public sealed record ControllerUnit(
    ControllerRole Role,
    string Callsign,
    int FrequencyKhz,
    IAirspaceVolume? Area = null,
    string? VoiceId = null,
    IReadOnlyList<int>? OtherFrequenciesKhz = null)
{
    /// <summary>True when <paramref name="khz"/> is this unit's frequency or one of its other frequencies.</summary>
    public bool Answers(int khz) => khz == FrequencyKhz || (OtherFrequenciesKhz?.Contains(khz) ?? false);
}
