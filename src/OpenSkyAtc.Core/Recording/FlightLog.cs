using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using OpenSkyAtc.Core.Airspace;
using OpenSkyAtc.Core.Brain;
using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Core.Recording;

/// <summary>
/// Everything needed to rerun a live flight exactly (for fixing what the owner saw): the start (plan, both airports
/// as the sim gave them, CENTER frequencies, seed, readback time), then, in order, every sim state the brain ticked,
/// every pilot call and every "ATC call finished playing" (which restarts the readback timer). Pilot calls and heard
/// events carry the number of states ticked before them, so a replay delivers them between the same two ticks.
/// </summary>
public sealed record FlightLogStart(
    FlightPlan Plan,
    FacilityDump Origin,
    FacilityDump Destination,
    IReadOnlyList<CenterFrequency> CenterFrequencies,
    int Seed,
    double ReadbackSeconds,
    DateTime StartedUtc,
    string? App);

public abstract record FlightLogEvent(int AfterStates);

/// <summary>A pilot call (button, voice or panel).</summary>
public sealed record LoggedIntent(int AfterStates, PilotIntent Intent) : FlightLogEvent(AfterStates);

/// <summary>The radio finished playing the <paramref name="AtcIndex"/>-th ATC transmission of the flight (0-based).</summary>
public sealed record LoggedHeard(int AfterStates, int AtcIndex) : FlightLogEvent(AfterStates);

public sealed record FlightLog(FlightLogStart Start, IReadOnlyList<SimState> States, IReadOnlyList<FlightLogEvent> Events);

/// <summary>
/// The flight log as JSON lines: {"k":"start",...}, then {"k":"state",...}, {"k":"intent","after":N,"type":"RequestTaxi",
/// "data":{...}} and {"k":"heard","after":N,"atc":I} lines in the order they happened. Core converts lines only; the
/// host writes the file (and keeps writing during the flight, so a crash still leaves a usable log).
/// </summary>
public static class FlightLogFormat
{
    public const string FormatName = "openskyatc-flightlog";
    public const int Version = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static readonly Dictionary<string, Type> IntentTypes = typeof(PilotIntent).Assembly.GetTypes()
        .Where(t => t.IsSubclassOf(typeof(PilotIntent)) && !t.IsAbstract)
        .ToDictionary(t => t.Name);

    public static string StartLine(FlightLogStart start)
    {
        var node = JsonSerializer.SerializeToNode(start, Options)!.AsObject();
        node.Insert(0, "k", "start");
        node.Insert(1, "format", FormatName);
        node.Insert(2, "version", Version);
        return node.ToJsonString();
    }

    public static string StateLine(SimState state)
    {
        var node = JsonSerializer.SerializeToNode(state, Options)!.AsObject();
        node.Insert(0, "k", "state");
        return node.ToJsonString();
    }

    public static string IntentLine(int afterStates, PilotIntent intent) =>
        new JsonObject
        {
            ["k"] = "intent",
            ["after"] = afterStates,
            ["type"] = intent.GetType().Name,
            ["data"] = JsonSerializer.SerializeToNode(intent, intent.GetType(), Options),
        }.ToJsonString();

    public static string HeardLine(int afterStates, int atcIndex) =>
        new JsonObject { ["k"] = "heard", ["after"] = afterStates, ["atc"] = atcIndex }.ToJsonString();

    /// <summary>Parses a log; a last line cut off by a crash is ignored.</summary>
    public static FlightLog Parse(IEnumerable<string> lines)
    {
        FlightLogStart? start = null;
        var states = new List<SimState>();
        var events = new List<FlightLogEvent>();
        var list = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        for (int i = 0; i < list.Count; i++)
        {
            JsonObject node;
            try
            {
                node = JsonNode.Parse(list[i])!.AsObject();
            }
            catch (JsonException) when (i == list.Count - 1)
            {
                break; // the app stopped in the middle of writing it
            }
            string kind = node["k"]?.GetValue<string>() ?? throw new FormatException($"Line {i + 1} has no kind.");
            node.Remove("k");
            switch (kind)
            {
                case "start":
                    if (node["format"]?.GetValue<string>() != FormatName) throw new FormatException("Not an OpenSkyATC flight log.");
                    if (node["version"]?.GetValue<int>() != Version) throw new FormatException($"Unsupported flight log version {node["version"]}.");
                    start = node.Deserialize<FlightLogStart>(Options);
                    break;
                case "state":
                    states.Add(node.Deserialize<SimState>(Options) ?? throw new FormatException($"Line {i + 1} is not a state."));
                    break;
                case "intent":
                    string type = node["type"]?.GetValue<string>() ?? "";
                    if (!IntentTypes.TryGetValue(type, out var t)) throw new FormatException($"Line {i + 1}: unknown pilot call '{type}'.");
                    var intent = (PilotIntent?)node["data"].Deserialize(t, Options) ?? throw new FormatException($"Line {i + 1} has no data.");
                    events.Add(new LoggedIntent(node["after"]!.GetValue<int>(), intent));
                    break;
                case "heard":
                    events.Add(new LoggedHeard(node["after"]!.GetValue<int>(), node["atc"]!.GetValue<int>()));
                    break;
                default:
                    throw new FormatException($"Line {i + 1}: unknown kind '{kind}'.");
            }
        }
        return new FlightLog(start ?? throw new FormatException("The flight log has no start line."), states, events);
    }

    /// <summary>"0:12:05.25" style time for messages.</summary>
    internal static string Time(TimeSpan t) => t.ToString(@"h\:mm\:ss\.ff", CultureInfo.InvariantCulture);
}

/// <summary>Reruns a logged flight through a new brain with the same inputs, in the same order.</summary>
public static class FlightLogReplayer
{
    /// <param name="options">The brain options the host used, without CENTER frequencies or timing (those come from the log).</param>
    /// <param name="airports">Airports to use instead of the logged sim data (tests on synthetic airports).</param>
    public static AtcBrain Replay(FlightLog log, AtcBrainOptions? options = null, (AirportData Origin, AirportData Destination)? airports = null)
    {
        var s = log.Start;
        var brain = new AtcBrain((options ?? new AtcBrainOptions()) with
        {
            CenterFrequencies = s.CenterFrequencies,
            Timing = (options?.Timing ?? new ConversationTiming()) with { ReadbackTimeout = TimeSpan.FromSeconds(s.ReadbackSeconds) },
        });
        var (origin, destination) = airports ?? (FacilityConverter.ToAirportData(s.Origin), FacilityConverter.ToAirportData(s.Destination));
        brain.Start(s.Plan, origin, destination, s.Seed);

        var atc = new List<Messages.AtcMessage>();
        brain.Transmission += atc.Add;
        int next = 0;
        void Deliver(int ticked)
        {
            for (; next < log.Events.Count && log.Events[next].AfterStates <= ticked; next++)
            {
                switch (log.Events[next])
                {
                    case LoggedIntent i: brain.HandlePilotIntent(i.Intent); break;
                    case LoggedHeard h when h.AtcIndex < atc.Count: brain.TransmissionHeard(atc[h.AtcIndex]); break;
                }
            }
        }
        Deliver(0);
        for (int i = 0; i < log.States.Count; i++)
        {
            brain.Tick(log.States[i], log.States[i].Time);
            Deliver(i + 1);
        }
        return brain;
    }
}

/// <summary>
/// Writes a flight log while the flight runs: call <see cref="Ticked"/> after each brain tick, <see cref="Intent"/>
/// when a pilot call is handed to the brain and <see cref="Heard"/> when the radio finished an ATC call. Each line goes
/// to <paramref name="writeLine"/> at once. Thread-safe.
/// </summary>
public sealed class FlightRecorder(Action<string> writeLine)
{
    private readonly object _lock = new();
    private readonly List<Messages.AtcMessage> _atc = [];
    private int _states;

    public void Begin(FlightLogStart start, AtcBrain brain)
    {
        lock (_lock) writeLine(FlightLogFormat.StartLine(start));
        brain.Transmission += m => { lock (_lock) _atc.Add(m); };
    }

    public void Ticked(SimState state)
    {
        lock (_lock)
        {
            writeLine(FlightLogFormat.StateLine(state));
            _states++;
        }
    }

    public void Intent(PilotIntent intent)
    {
        lock (_lock) writeLine(FlightLogFormat.IntentLine(_states, intent));
    }

    public void Heard(Messages.AtcMessage message)
    {
        lock (_lock)
        {
            int index = _atc.FindIndex(m => ReferenceEquals(m, message));
            if (index >= 0) writeLine(FlightLogFormat.HeardLine(_states, index));
        }
    }
}
