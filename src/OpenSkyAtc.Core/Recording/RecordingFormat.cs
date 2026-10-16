using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Core.Recording;

/// <summary>First line of a recording file.</summary>
public sealed record RecordingHeader(string Format, int Version, DateTime StartedUtc, string? Aircraft)
{
    public const string FormatName = "openskyatc-recording";
    public const int CurrentVersion = 1;
}

/// <summary>
/// JSON-lines recording format: one <see cref="RecordingHeader"/> line, then one <see cref="SimState"/> per line.
/// Converts lines only; reading and writing files is left to the caller so Core stays free of I/O.
/// </summary>
public static class RecordingFormat
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase), new TimeSpanSecondsConverter() },
    };

    public static string HeaderLine(string? aircraft, DateTime startedUtc) =>
        JsonSerializer.Serialize(
            new RecordingHeader(RecordingHeader.FormatName, RecordingHeader.CurrentVersion, startedUtc, aircraft), Options);

    public static string StateLine(SimState state) => JsonSerializer.Serialize(state, Options);

    /// <summary>Parses a whole recording. Blank lines are skipped.</summary>
    public static (RecordingHeader Header, IReadOnlyList<SimState> States) Parse(IEnumerable<string> lines)
    {
        using var e = lines.Where(l => !string.IsNullOrWhiteSpace(l)).GetEnumerator();
        if (!e.MoveNext()) throw new FormatException("Recording is empty.");

        var header = JsonSerializer.Deserialize<RecordingHeader>(e.Current, Options)
            ?? throw new FormatException("Recording header is missing.");
        if (header.Format != RecordingHeader.FormatName)
            throw new FormatException($"Not an OpenSkyATC recording (format '{header.Format}').");
        if (header.Version != RecordingHeader.CurrentVersion)
            throw new FormatException($"Unsupported recording version {header.Version}.");

        var states = new List<SimState>();
        int lineNo = 1;
        while (e.MoveNext())
        {
            lineNo++;
            states.Add(JsonSerializer.Deserialize<SimState>(e.Current, Options)
                ?? throw new FormatException($"Line {lineNo} is not a SimState."));
        }
        return (header, states);
    }

    /// <summary>Writes TimeSpan as seconds (e.g. 12.25) so recordings are easy to read and diff.</summary>
    private sealed class TimeSpanSecondsConverter : JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            TimeSpan.FromSeconds(reader.GetDouble());

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options) =>
            writer.WriteRawValue(value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
    }
}
