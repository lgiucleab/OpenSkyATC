using System.Globalization;
using System.Runtime.CompilerServices;
using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Host;
using OpenSkyAtc.SimBrief;

namespace OpenSkyAtc.Recorder;

/// <summary>
/// "live": a flight in a console, on the same <see cref="FlightHost"/> as the app (step 13). Prints every ATC call
/// and takes pilot calls from number keys; useful for checklists and scripted runs (keys can be piped in).
/// </summary>
internal static class LiveCommand
{
    public const string Usage =
        "live (--simbrief PILOT_ID | --pln FILE | --from ICAO --to ICAO --callsign VLG1234)\n" +
        "     [--type A320] [--cruise 350] [--sid NAME] [--star NAME] [--stand NAME]   (override the imported plan)\n" +
        "     [--ptt \"VK_LCONTROL+T\" | --ptt \"joystick:0:button:3\"] [--no-atc-key] [--seed N]\n" +
        "     [--no-arrows] [--arrow-lookahead M] [--arrow-spacing M] [--no-voice] [--volume 0..1]";

    public static (FlightRequest Request, AppSettings Settings)? Parse(string[] a)
    {
        var settings = new AppSettings();
        var request = new FlightRequest();
        for (int i = 1; i < a.Length; i++)
        {
            string? Next() => i + 1 < a.Length ? a[++i] : null;
            switch (a[i])
            {
                case "--simbrief": request = request with { Source = PlanSource.SimBrief, SimBriefPilotId = Next() }; break;
                case "--pln": request = request with { Source = PlanSource.PlnFile, PlnPath = Next() }; break;
                case "--from": request = request with { From = Next()?.ToUpperInvariant() }; break;
                case "--to": request = request with { To = Next()?.ToUpperInvariant() }; break;
                case "--callsign": request = request with { Callsign = Next()?.ToUpperInvariant() }; break;
                case "--type": request = request with { AircraftType = Next() }; break;
                case "--cruise": request = request with { Cruise = AltitudeTarget.FlightLevel(int.Parse(Next() ?? "", CultureInfo.InvariantCulture)) }; break;
                case "--sid": request = request with { Sid = Next() }; break;
                case "--star": request = request with { Star = Next() }; break;
                case "--stand": request = request with { Stand = Next() }; break;
                case "--seed": request = request with { Seed = int.Parse(Next() ?? "", CultureInfo.InvariantCulture) }; break;
                case "--ptt": settings = settings with { PushToTalkInput = Next() }; break;
                case "--no-atc-key": settings = settings with { UseAtcKey = false }; break;
                case "--no-arrows": settings = settings with { TaxiArrows = false }; break;
                case "--arrow-lookahead": settings = settings with { ArrowLookAheadM = double.Parse(Next() ?? "", CultureInfo.InvariantCulture) }; break;
                case "--arrow-spacing": settings = settings with { ArrowSpacingM = double.Parse(Next() ?? "", CultureInfo.InvariantCulture) }; break;
                case "--no-voice": settings = settings with { VoiceEnabled = false }; break;
                case "--volume": settings = settings with { Volume = float.Parse(Next() ?? "", CultureInfo.InvariantCulture) }; break;
                default: return null;
            }
        }
        bool hasPlan = request.SimBriefPilotId is not null || request.PlnPath is not null || (request.From is not null && request.To is not null && request.Callsign is not null);
        return hasPlan ? (request, settings) : null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Run(FlightRequest request, AppSettings settings)
    {
        using var host = new FlightHost(settings);
        using var quit = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; quit.Cancel(); };
        host.StatusChanged += (status, detail) => Console.WriteLine($"[{status}] {detail}");
        host.Notice += n => Console.WriteLine($"[note] {n}");
        host.Transcript += lines =>
        {
            foreach (var l in lines)
            {
                string who = l.Kind switch
                {
                    TranscriptKind.Atc => $"ATC  {l.Station} ({Mhz(l.FrequencyKhz)})",
                    TranscriptKind.Pilot => $"YOU  ({Mhz(l.FrequencyKhz)})",
                    TranscriptKind.Warning => "  ! ",
                    _ => "    ",
                };
                Console.WriteLine($"{l.Time:hh\\:mm\\:ss} {who}: {l.Text}");
            }
        };

        try
        {
            host.StartAsync(request).GetAwaiter().GetResult();
        }
        catch (FlightPlanImportException e)
        {
            Console.Error.WriteLine($"Flight plan import failed ({e.Error}): {e.Message}");
            return 1;
        }
        catch (Exception e) when (e is FileNotFoundException or InvalidOperationException or HttpRequestException or System.Runtime.InteropServices.COMException)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
        if (host.Status != HostStatus.Running) return 1;
        var plan = host.Snapshot().Plan!;
        Console.WriteLine($"Plan: {plan.Callsign} {plan.AircraftType} {plan.Origin} {plan.DepartureRunway} {plan.PlannedSid} -> {plan.Destination} {plan.PlannedStar} {plan.ArrivalRunway}, cruise {plan.CruiseLevel}, {plan.Route.Count} route points");
        Console.WriteLine("Keys: 1-9 send a pilot call from the list, L lists them again, A turns taxi arrows on/off, Q quits.");

        string shown = "";
        while (!quit.IsCancellationRequested)
        {
            var snap = host.Snapshot();
            var intents = snap.Intents.Take(9).ToList();
            string list = string.Join(" | ", intents.Select((x, i) => $"{i + 1} {PilotPhrase.Of(x)}"));
            if (snap.Expected is { } expected)
                list += $"   -> call {expected.Callsign} {Mhz(expected.FrequencyKhz)}" +
                        (snap.TunedKhz != 0 && !expected.Answers(snap.TunedKhz) ? $" (you are on {Mhz(snap.TunedKhz)})" : "");
            if (list != shown)
            {
                Console.WriteLine($"[you] {(intents.Count == 0 ? "(nothing to say)" : list)}");
                shown = list;
            }

            if (NextKey() is not { } c)
            {
                quit.Token.WaitHandle.WaitOne(100);
                continue;
            }
            if (c is 'q' or 'Q') break;
            if (c is 'l' or 'L') { shown = ""; continue; }
            if (c is 'a' or 'A')
            {
                settings = settings with { TaxiArrows = !settings.TaxiArrows };
                host.Apply(settings);
                Console.WriteLine($"[arrows] turned {(settings.TaxiArrows ? "on" : "off")}");
                continue;
            }
            int n = c - '1';
            if (n >= 0 && n < intents.Count) host.Send(intents[n]);
        }
        host.Stop();
        Console.WriteLine("Stopped.");
        return 0;
    }

    private static string Mhz(int? khz) => khz is { } k ? (k / 1000.0).ToString("0.000", CultureInfo.InvariantCulture) : "";

    private static readonly System.Collections.Concurrent.ConcurrentQueue<char> Piped = new();
    private static Thread? _pipeReader;

    /// <summary>A key press, or with input piped in (scripted runs) the next character of it; null if none yet.</summary>
    private static char? NextKey()
    {
        if (!Console.IsInputRedirected) return Console.KeyAvailable ? Console.ReadKey(intercept: true).KeyChar : null;
        _pipeReader ??= StartPipeReader();
        return Piped.TryDequeue(out char c) ? c : null;
    }

    private static Thread StartPipeReader()
    {
        var t = new Thread(() =>
        {
            int c;
            while ((c = Console.In.Read()) >= 0) if (!char.IsWhiteSpace((char)c)) Piped.Enqueue((char)c);
        }) { IsBackground = true, Name = "stdin" };
        t.Start();
        return t;
    }
}
