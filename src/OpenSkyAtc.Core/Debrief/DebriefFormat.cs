using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSkyAtc.Core.Debrief;

/// <summary>A debrief as JSON text (enums as names, times as "hh:mm:ss"). Core converts text only; the host saves files.</summary>
public static class DebriefFormat
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ToJson(FlightDebrief debrief) => JsonSerializer.Serialize(debrief, Options);

    /// <summary>The debrief in <paramref name="json"/>; null when it is not one (or a newer version).</summary>
    public static FlightDebrief? FromJson(string json)
    {
        try
        {
            var debrief = JsonSerializer.Deserialize<FlightDebrief>(json, Options);
            return debrief is { Version: 1 } ? debrief : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
