using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSkyAtc.Host;

/// <summary>A button in the panel: <see cref="Key"/> identifies the intent it stood for when it was sent.</summary>
public sealed record PanelButton(int Id, string Key, string Label, bool Primary, bool Enabled = true);

public sealed record PanelCall(string Station, string Text);

/// <summary>
/// What the in-sim toolbar panel shows (step 15, like the app's compact strip). Built by the app with its own
/// language, sent as JSON over the CommBus (event "OpenSkyATC.State"); toolbar/opensky-atc-toolbar renders it.
/// </summary>
public sealed record PanelState
{
    public int Version { get; init; } = PanelProtocol.Version;
    public bool Flight { get; init; }
    public bool Sim { get; init; }
    public bool Paused { get; init; }
    public string Status { get; init; } = "";
    public string Ptt { get; init; } = "";
    public bool Transmitting { get; init; }
    public string Callsign { get; init; } = "";
    public string Frequency { get; init; } = "";
    public string TalkingTo { get; init; } = "";
    public string? Hint { get; init; }
    public bool HintWarning { get; init; }
    public IReadOnlyList<PanelCall> Calls { get; init; } = [];
    public IReadOnlyList<PanelButton> Buttons { get; init; } = [];
    public bool Arrows { get; init; }
    public string ArrowsLabel { get; init; } = "";

    /// <summary>Shown in hard mode instead of a readback button: read back by voice.</summary>
    public string? VoiceReadback { get; init; }
}

/// <summary>Commands from the panel (event "OpenSkyATC.Command").</summary>
public abstract record PanelCommand
{
    /// <summary>The panel opened: send the state now.</summary>
    public sealed record Hello : PanelCommand;

    /// <summary>A button: <see cref="Key"/> must still match the button with that id, or the press is stale.</summary>
    public sealed record Intent(int Id, string Key) : PanelCommand;

    public sealed record Arrows(bool On) : PanelCommand;
}

public static class PanelProtocol
{
    public const int Version = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(PanelState state) => JsonSerializer.Serialize(state, Json);

    /// <summary>{"cmd":"hello"}, {"cmd":"intent","id":2,"key":"2:RequestTaxi"}, {"cmd":"arrows","on":false}; null when malformed.</summary>
    public static PanelCommand? Parse(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("cmd", out var cmd)) return null;
            return cmd.GetString() switch
            {
                "hello" => new PanelCommand.Hello(),
                "intent" when root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out int n)
                              && root.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.String
                    => new PanelCommand.Intent(n, key.GetString()!),
                "arrows" when root.TryGetProperty("on", out var on) && on.ValueKind is JsonValueKind.True or JsonValueKind.False
                    => new PanelCommand.Arrows(on.GetBoolean()),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The key of the intent at <paramref name="index"/>: its position and kind.</summary>
    public static string Key(int index, Core.Conversation.PilotIntent intent) => $"{index}:{intent.GetType().Name}";
}

/// <summary>
/// When to send the panel state: on a change (at most twice a second), else every 2 s as a heartbeat so the panel
/// knows the app is there. While the sim is paused the panel's JavaScript stops and CommBus messages queue up, so
/// then at most one message every 5 s.
/// </summary>
public sealed class PanelLink
{
    public static readonly TimeSpan MinGap = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan PausedGap = TimeSpan.FromSeconds(5);

    private string? _last;
    private TimeSpan _sentAt = TimeSpan.MinValue;

    /// <summary>Forget what was sent (the panel opened, or the sim reconnected): the next state goes out at once.</summary>
    public void Reset()
    {
        _last = null;
        _sentAt = TimeSpan.MinValue;
    }

    public bool ShouldSend(string json, TimeSpan now, bool paused)
    {
        var since = _sentAt == TimeSpan.MinValue ? TimeSpan.MaxValue : now - _sentAt;
        bool send = paused
            ? since >= PausedGap
            : (json != _last && since >= MinGap) || since >= Heartbeat;
        if (!send) return false;
        _last = json;
        _sentAt = now;
        return true;
    }
}
