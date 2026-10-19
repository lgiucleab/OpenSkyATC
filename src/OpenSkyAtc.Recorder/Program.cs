using System.Runtime.CompilerServices;
using OpenSkyAtc.Core.Recording;
using OpenSkyAtc.SimConnect;

namespace OpenSkyAtc.Recorder;

/// <summary>
/// Records the user aircraft's SimState at 4 Hz to a JSON-lines file.
/// Usage: OpenSkyAtc.Recorder [--out file.jsonl] [--simconnect-dir folder]
/// </summary>
internal static class Program
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] is "export" or "survey-center" or "taxi") return Facilities(args);
        if (args.Length > 0 && args[0] == "live") return Live(args);
        if (args.Length > 0 && args[0] == "voices") return VoiceCommands.Run(args);
        if (args.Length > 0 && args[0] == "stt") return SttCommands.Run(args);
        if (args.Length > 0 && args[0] == "replay-log") return ReplayLogCommand.Run(args);

        string? output = null;
        string? simConnectDir = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out" when i + 1 < args.Length: output = args[++i]; break;
                case "--simconnect-dir" when i + 1 < args.Length: simConnectDir = args[++i]; break;
                case "--help" or "-h":
                    Console.WriteLine("Usage: OpenSkyAtc.Recorder [--out file.jsonl] [--simconnect-dir folder]");
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown argument '{args[i]}'. Use --help.");
                    return 2;
            }
        }
        output ??= $"recording-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl";

        try
        {
            SimConnectLibrary.EnsureLoaded(simConnectDir);
        }
        catch (FileNotFoundException e)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }

        return Record(output);
    }

    /// <summary>
    /// "export ICAO [--out file]" writes the airport's facility data as JSON;
    /// "survey-center PREFIX [--max N] [--out file]" counts CENTER frequencies at airports whose ICAO starts with PREFIX.
    /// </summary>
    private static int Facilities(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: OpenSkyAtc.Recorder export ICAO [--out file] | survey-center PREFIX [--max N] [--out file]");
            return 2;
        }
        string? output = null;
        int max = 500;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--out" && i + 1 < args.Length) output = args[++i];
            else if (args[i] == "--max" && i + 1 < args.Length) max = int.Parse(args[++i]);
        }
        try
        {
            SimConnectLibrary.EnsureLoaded();
        }
        catch (FileNotFoundException e)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
        try
        {
            return args[0] switch
            {
                "export" => FacilityCommands.Export(args[1], output),
                "taxi" => FacilityCommands.Taxi(args[1]),
                _ => FacilityCommands.SurveyCenter(args[1], max, output),
            };
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            Console.Error.WriteLine("Could not connect to MSFS 2024. Is the sim running?");
            return 1;
        }
    }

    /// <summary>"live --from ICAO --to ICAO --callsign X ...": the brain against the running sim (step 9 checklist).</summary>
    private static int Live(string[] args)
    {
        if (LiveCommand.Parse(args) is not { } live)
        {
            Console.Error.WriteLine("Usage: OpenSkyAtc.Recorder " + LiveCommand.Usage);
            return 2;
        }
        try
        {
            SimConnectLibrary.EnsureLoaded();
        }
        catch (FileNotFoundException e)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
        return LiveCommand.Run(live.Request, live.Settings);
    }

    // Kept separate from Main so no SimConnect type is touched before the DLLs are loaded.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Record(string output)
    {
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };

        var source = new SimBridge(new SimBridgeOptions { Interval = Interval, ClientName = "OpenSkyATC Recorder", UseAtcKey = false });
        Console.WriteLine("Waiting for MSFS 2024... (Ctrl+C to quit)");

        using var writer = new StreamWriter(output, append: false);
        bool headerWritten = false;
        long samples = 0;
        var pending = new List<string>();

        void WriteHeader(string? aircraft)
        {
            if (headerWritten) return;
            writer.WriteLine(RecordingFormat.HeaderLine(aircraft, DateTime.UtcNow));
            headerWritten = true;
            foreach (var line in pending) writer.WriteLine(line);
            pending.Clear();
        }

        source.TitleReceived += title => { WriteHeader(title); writer.Flush(); };
        source.StateReceived += state =>
        {
            string line = RecordingFormat.StateLine(state);
            if (headerWritten) writer.WriteLine(line); else pending.Add(line);
            writer.Flush();
            if (++samples % 40 == 0)
                Console.Write($"\r{samples} samples, {state.Time:hh\\:mm\\:ss}, {state.AltitudeMslFt:0} ft, {state.GroundSpeedKt:0} kt   ");
        };
        source.Disconnected += () => Console.WriteLine("\nMSFS closed the connection.");

        Console.WriteLine($"Connected. Recording to {Path.GetFullPath(output)}");
        source.Run(cancel.Token);
        WriteHeader(null);

        Console.WriteLine($"\nStopped. {samples} samples written.");
        return 0;
    }
}
