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

    // Kept separate from Main so no SimConnect type is touched before the DLLs are loaded.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Record(string output)
    {
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };

        using var source = new SimStateSource(Interval);
        Console.WriteLine("Waiting for MSFS 2024... (Ctrl+C to quit)");
        while (!source.TryConnect("OpenSkyATC Recorder"))
        {
            if (cancel.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5))) return 0;
        }

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
