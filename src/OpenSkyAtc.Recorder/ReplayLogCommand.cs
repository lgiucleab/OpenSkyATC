using OpenSkyAtc.Core.Brain;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Recording;
using OpenSkyAtc.Host;
using OpenSkyAtc.Phraseology;

namespace OpenSkyAtc.Recorder;

/// <summary>
/// "replay-log FILE [--out transcript.txt]": reruns a flight log written by the app (Gear: none needed; the app writes
/// one per flight to %LOCALAPPDATA%\OpenSkyATC\recordings) through a new brain with the same inputs, and prints the
/// transcript: every ATC call, pilot call, phase change, readback verdict and warning, with sim times. Used to
/// reproduce what a pilot saw before fixing it. Needs no sim.
/// </summary>
internal static class ReplayLogCommand
{
    public const string Usage = "replay-log FILE.flightlog.jsonl [--out transcript.txt]";

    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: " + Usage);
            return 2;
        }
        string file = args[1];
        int o = Array.IndexOf(args, "--out");
        string? output = o >= 0 && o + 1 < args.Length ? args[o + 1] : null;

        FlightLog log;
        try
        {
            log = FlightLogFormat.Parse(File.ReadLines(file));
        }
        catch (Exception e) when (e is IOException or FormatException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"{file}: {e.Message}");
            return 1;
        }

        var data = DataBundle.Load();
        var brain = FlightLogReplayer.Replay(log, new AtcBrainOptions { Squawks = data.Squawks, Sops = data.Sops });
        var s = log.Start;
        var lines = new List<string>
        {
            $"# {s.Plan.Callsign} {s.Plan.Origin} -> {s.Plan.Destination}, seed {s.Seed}, {log.States.Count} states, " +
            $"{log.Events.OfType<LoggedIntent>().Count()} pilot calls, recorded {s.StartedUtc:u} by {s.App}",
        };
        var writer = new TranscriptWriter(brain.Session, new IcaoRenderer(s.Seed, callsigns: data.Callsigns), null);
        foreach (var l in writer.TakeNew())
            lines.Add($"{l.Time:hh\\:mm\\:ss} {l.Kind,-7} {(l.Station is null ? "" : l.Station + ": ")}{l.Text}");
        lines.Add($"# ended: {brain.Session.Ended}, last phase {brain.Session.Phase}");

        if (output is not null)
        {
            File.WriteAllLines(output, lines);
            Console.WriteLine($"{lines.Count} lines written to {output}");
        }
        else
        {
            foreach (var l in lines) Console.WriteLine(l);
        }
        return 0;
    }
}
