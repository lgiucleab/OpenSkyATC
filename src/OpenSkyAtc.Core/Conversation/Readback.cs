using System.Globalization;
using OpenSkyAtc.Core.Messages;
using OpenSkyAtc.Core.Model;

namespace OpenSkyAtc.Core.Conversation;

/// <summary>Items an instruction can require in its readback (design doc readback table).</summary>
public enum ReadbackItem
{
    Destination,
    Route,
    InitialClimb,
    Squawk,
    HoldingPoint,
    Runway,
    HoldShorts,

    /// <summary>The clearance itself: line up, takeoff, landing or crossing.</summary>
    Clearance,

    Altitude,
    Heading,
    Speed,
    DirectTo,
    Frequency,
    Qnh,
}

/// <summary>
/// Canonical readback values, shared by the brain (expected values), click mode (generated readbacks)
/// and speech recognition (parsed readbacks), so all three compare the same way.
/// </summary>
public static class ReadbackValues
{
    public const string FlightPlannedRoute = "FLIGHT PLANNED ROUTE";
    public const string LineUp = "LINEUP";
    public const string Takeoff = "TAKEOFF";

    public static string Destination(string icao) => Normalize(icao);
    public static string Route(string? sid) => sid is null ? FlightPlannedRoute : Normalize(sid);
    public static string Altitude(AltitudeTarget alt) => alt.Kind == AltitudeKind.FlightLevel ? $"FL{alt.Value:000}" : $"{alt.Value}FT";
    public static string Squawk(int code) => code.ToString("0000", CultureInfo.InvariantCulture);
    public static string Runway(string designator) => Normalize(designator);
    public static string HoldingPoint(string name) => Normalize(name);
    public static string HoldShorts(IEnumerable<string> runways) => string.Join(",", runways.Select(Normalize));
    public static string Frequency(int khz) => khz.ToString(CultureInfo.InvariantCulture);

    public static string Normalize(string value) => value.Trim().ToUpperInvariant();
}

/// <summary>What an instruction expects to hear back, and how to correct each item.</summary>
/// <param name="Items">Required items and their expected canonical values, in instruction order.</param>
/// <param name="Corrections">Message parts that restate each item when it is read back wrongly.</param>
public sealed record ReadbackExpectation(
    IReadOnlyList<KeyValuePair<ReadbackItem, string>> Items,
    IReadOnlyDictionary<ReadbackItem, IReadOnlyList<AtcMessagePart>> Corrections)
{
    public static ReadbackExpectationBuilder Builder() => new();
}

public sealed class ReadbackExpectationBuilder
{
    private readonly List<KeyValuePair<ReadbackItem, string>> _items = [];
    private readonly Dictionary<ReadbackItem, IReadOnlyList<AtcMessagePart>> _corrections = [];

    public ReadbackExpectationBuilder Add(ReadbackItem item, string value, params AtcMessagePart[] correction)
    {
        _items.Add(new(item, value));
        _corrections[item] = correction;
        return this;
    }

    public ReadbackExpectation Build() => new(_items.ToArray(), _corrections);
}

public sealed record ReadbackVerdict(IReadOnlyList<ReadbackItem> Wrong, IReadOnlyList<ReadbackItem> Missing)
{
    public bool IsCorrect => Wrong.Count == 0 && Missing.Count == 0;
}

public static class ReadbackVerifier
{
    /// <summary>
    /// Compares a readback with the outstanding items. Extra items are ignored. Hold-shorts compare as a set,
    /// everything else as normalized text.
    /// </summary>
    public static ReadbackVerdict Check(IReadOnlyList<KeyValuePair<ReadbackItem, string>> expected, IReadOnlyDictionary<ReadbackItem, string> given)
    {
        var wrong = new List<ReadbackItem>();
        var missing = new List<ReadbackItem>();
        foreach (var (item, value) in expected)
        {
            if (!given.TryGetValue(item, out var heard) || string.IsNullOrWhiteSpace(heard)) missing.Add(item);
            else if (!Matches(item, value, heard)) wrong.Add(item);
        }
        return new ReadbackVerdict(wrong, missing);
    }

    private static bool Matches(ReadbackItem item, string expected, string heard)
    {
        if (item == ReadbackItem.HoldShorts)
        {
            static HashSet<string> Set(string s) => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ReadbackValues.Normalize).ToHashSet();
            return Set(expected).SetEquals(Set(heard));
        }
        return ReadbackValues.Normalize(expected) == ReadbackValues.Normalize(heard);
    }
}
