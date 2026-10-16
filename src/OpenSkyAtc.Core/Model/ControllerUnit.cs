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
public sealed record ControllerUnit(
    ControllerRole Role,
    string Callsign,
    int FrequencyKhz,
    IAirspaceVolume? Area = null,
    string? VoiceId = null);
