using System.Text;
using System.Text.Json.Nodes;
using OpenSkyAtc.Core.Recording;

namespace OpenSkyAtc.Host;

/// <summary>
/// The audio of each voice call, kept with the flight log (owner's choice, after the first flight): call-NNN.wav
/// (16 kHz mono, 16-bit) and one line per call in calls.jsonl (what Whisper heard, the prompt, the calls available
/// then and how it was understood), so recognition can be rerun on the owner's own voice (`Recorder stt rerun`).
/// On this PC only, deleted with the log.
/// </summary>
public sealed class VoiceClipWriter(string folder)
{
    public const string IndexFile = "calls.jsonl";

    private readonly object _lock = new();
    private int _count;

    public string Folder { get; } = folder;

    public void Save(int afterStates, HeardCall call, string model)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Folder);
            string clip = $"call-{++_count:000}.wav";
            WriteWav(Path.Combine(Folder, clip), call.Audio);
            var line = new JsonObject
            {
                ["after"] = afterStates,
                ["clip"] = clip,
                ["model"] = model,
                ["text"] = call.Text,
                ["confidence"] = Math.Round(call.Confidence, 3),
                ["seconds"] = Math.Round(call.Took.TotalSeconds, 2),
                ["prompt"] = call.Prompt,
                ["intent"] = FlightLogFormat.IntentJson(call.Intent),
                ["available"] = new JsonArray((call.Context?.Available ?? []).Select(i => (JsonNode)FlightLogFormat.IntentJson(i)).ToArray()),
            };
            File.AppendAllText(Path.Combine(Folder, IndexFile), line.ToJsonString() + "\n", new UTF8Encoding(false));
        }
    }

    /// <summary>16-bit PCM WAV, mono.</summary>
    public static void WriteWav(string path, float[] samples, int rate = 16_000)
    {
        using var w = new BinaryWriter(File.Create(path));
        int bytes = samples.Length * 2;
        w.Write("RIFF"u8);
        w.Write(36 + bytes);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1); // PCM
        w.Write((short)1); // mono
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(bytes);
        foreach (float s in samples) w.Write((short)Math.Round(Math.Clamp(s, -1f, 1f) * short.MaxValue));
    }

    /// <summary>The lines of a clip folder's index.</summary>
    public static IEnumerable<JsonObject> ReadIndex(string folder) =>
        File.ReadLines(Path.Combine(folder, IndexFile)).Where(l => l.Length > 0).Select(l => JsonNode.Parse(l)!.AsObject());
}
