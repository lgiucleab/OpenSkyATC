using OpenSkyAtc.Core.Conversation;

namespace OpenSkyAtc.Replay.Tests.Harness;

/// <summary>
/// Builds pilot intents for scripts, the way the click UI would: from the brain's available intents when
/// they carry generated values (readbacks, check-ins), with script arguments as overrides.
/// </summary>
public static class IntentFactory
{
    private static readonly string[] Names =
    [
        "RequestClearance", "RequestPushStart", "RequestTaxi", "ReadyForDeparture", "RejectTakeoff", "CheckIn",
        "RequestDescent", "ReportEstablished", "GoingAround", "ReportVacated", "Readback", "SayAgain", "Unreadable", "Wilco",
        "Unable", "Mayday", "PanPan",
    ];

    public static bool IsKnown(string name) => Names.Contains(name);

    public static PilotIntent Create(ScriptedIntent s, IReadOnlyList<PilotIntent> available) => s.Name switch
    {
        "Readback" => ReadbackWithOverrides(available.OfType<Readback>().FirstOrDefault(), s.Args),
        "CheckIn" => available.OfType<CheckIn>().FirstOrDefault() ?? new CheckIn(),
        "RequestClearance" => new RequestClearance(s.Args.GetValueOrDefault("atis")),
        "RequestPushStart" => new RequestPushStart(),
        "RequestTaxi" => new RequestTaxi(),
        "ReadyForDeparture" => new ReadyForDeparture(),
        "RejectTakeoff" => new RejectTakeoff(),
        "RequestDescent" => new RequestDescent(),
        "ReportEstablished" => new ReportEstablished(),
        "GoingAround" => new GoingAround(),
        "ReportVacated" => new ReportVacated(),
        "SayAgain" => new SayAgain(),
        "Unreadable" => new Unreadable(),
        "Wilco" => new Wilco(),
        "Unable" => new Unable(s.Args.GetValueOrDefault("reason")),
        "Mayday" => new Mayday(),
        "PanPan" => new PanPan(),
        _ => throw new ArgumentException($"Unknown intent '{s.Name}'."),
    };

    /// <summary>The generated (correct) readback with items replaced ("squawk=1234") or removed ("-squawk").</summary>
    private static Readback ReadbackWithOverrides(Readback? generated, IReadOnlyDictionary<string, string?> args)
    {
        var items = generated?.Items.ToDictionary() ?? [];
        foreach (var (key, value) in args)
        {
            if (!Enum.TryParse<ReadbackItem>(key, ignoreCase: true, out var item))
                throw new ArgumentException($"Unknown readback item '{key}'.");
            if (value is null) items.Remove(item);
            else items[item] = value;
        }
        return new Readback(items, generated?.ExchangeId);
    }

    /// <summary>One-line description for transcripts.</summary>
    public static string Describe(PilotIntent intent) => intent switch
    {
        Readback r => r.Items.Count == 0 ? "Readback (nothing)" : "Readback " + string.Join(" ", r.Items.Select(kv => $"{kv.Key}={kv.Value}")),
        CheckIn c => $"CheckIn passing={c.AltitudeFt?.ToString() ?? "-"} cleared={c.ClearedAltitude?.ToString() ?? "-"}",
        RequestClearance { AtisLetter: { } a } => $"RequestClearance atis={a}",
        Unable { Reason: { } r } => $"Unable reason={r}",
        _ => intent.GetType().Name,
    };
}
