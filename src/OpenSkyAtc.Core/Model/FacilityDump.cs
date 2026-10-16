using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSkyAtc.Core.Model;

/// <summary>
/// An airport as the sim's facility data describes it, close to the SimConnect layout but with taxi points and
/// parking converted to latitude/longitude. Written by the exporter (live sim) and used as airport fixtures.
/// Enum values are the SimConnect numbers documented in the SDK (SimConnect_AddToFacilityDefinition).
/// </summary>
public sealed record FacilityDump
{
    public const string FormatName = "openskyatc-facility";
    public const int CurrentVersion = 1;

    public string Format { get; init; } = FormatName;
    public int Version { get; init; } = CurrentVersion;
    public DateTime ExportedUtc { get; init; }
    public string? SimVersion { get; init; }

    public required string Icao { get; init; }
    public string? Region { get; init; }
    public string? Name { get; init; }
    public string? City { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double ElevationM { get; init; }
    public double MagVarDeg { get; init; }
    public double TransitionAltitudeM { get; init; }
    public double TransitionLevelM { get; init; }

    public IReadOnlyList<FacilityRunway> Runways { get; init; } = [];
    public IReadOnlyList<FacilityFrequency> Frequencies { get; init; } = [];
    public IReadOnlyList<FacilityTaxiPoint> TaxiPoints { get; init; } = [];
    public IReadOnlyList<FacilityParking> Parkings { get; init; } = [];
    public IReadOnlyList<FacilityTaxiPath> TaxiPaths { get; init; } = [];
    public IReadOnlyList<string> TaxiNames { get; init; } = [];
    public IReadOnlyList<FacilityApproach> Approaches { get; init; } = [];
    public IReadOnlyList<string> Departures { get; init; } = [];
    public IReadOnlyList<string> Arrivals { get; init; } = [];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static FacilityDump Parse(string json)
    {
        var dump = JsonSerializer.Deserialize<FacilityDump>(json, Options) ?? throw new FormatException("Empty facility file.");
        if (dump.Format != FormatName) throw new FormatException($"Not an OpenSkyATC facility file (format '{dump.Format}').");
        if (dump.Version != CurrentVersion) throw new FormatException($"Unsupported facility file version {dump.Version}.");
        return dump;
    }
}

/// <param name="HeadingDeg">Heading of the primary end, as the sim reports it.</param>
/// <param name="PrimaryNumber">1-36 runway number, 37-44 compass directions (SimConnect).</param>
/// <param name="PrimaryDesignator">0 none, 1 L, 2 R, 3 C, 4 water, 5 A, 6 B.</param>
public sealed record FacilityRunway(
    double Latitude, double Longitude, double AltitudeM, double HeadingDeg, double LengthM, double WidthM,
    int PrimaryNumber, int PrimaryDesignator, int SecondaryNumber, int SecondaryDesignator,
    string? PrimaryIls, string? SecondaryIls);

/// <param name="Type">SimConnect frequency type: 1 ATIS ... 5 GROUND, 6 TOWER, 7 CLEARANCE, 8 APPROACH, 9 DEPARTURE, 10 CENTER.</param>
public sealed record FacilityFrequency(int Type, int FrequencyHz, string? Name);

/// <param name="Type">0 none, 1 normal, 2 hold short, 4 ILS hold short, 5/6 hold short not drawn.</param>
public sealed record FacilityTaxiPoint(int Index, int Type, int Orientation, double Latitude, double Longitude);

/// <param name="Name">0 none, 1 parking, 2-9 N..NW parking, 10 gate, 11 dock, 12-37 gate A-Z.</param>
public sealed record FacilityParking(
    int Index, int Type, int Name, int Suffix, uint Number, double HeadingDeg, double RadiusM, double Latitude, double Longitude);

/// <param name="Type">0 none, 1 taxi, 2 runway, 3 parking, 4 path, 5 closed, 6 vehicle, 7 road, 8 painted line.</param>
/// <param name="Start">Index of the taxi point (or parking spot) the path starts from.</param>
/// <param name="End">Index of the taxi point, or of the parking spot for parking paths.</param>
public sealed record FacilityTaxiPath(int Type, double WidthM, int RunwayNumber, int RunwayDesignator, int Start, int End, uint NameIndex);

/// <param name="Type">SimConnect approach type (0 undefined, 1 GPS, 2 VOR, 3 NDB, 4 ILS, 5 localizer, ...).</param>
public sealed record FacilityApproach(int Type, int Suffix, int RunwayNumber, int RunwayDesignator, string? FafIcao, double FafAltitudeM, double MissedAltitudeFt);
