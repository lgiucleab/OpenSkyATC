using System.Globalization;
using System.Runtime.CompilerServices;
using OpenSkyAtc.Core.Airspace;
using OpenSkyAtc.Core.Brain;
using OpenSkyAtc.Core.Config;
using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Conformance;
using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Phraseology;
using OpenSkyAtc.SimBrief;
using OpenSkyAtc.SimConnect;
using OpenSkyAtc.TaxiArrows;

namespace OpenSkyAtc.Recorder;

/// <summary>
/// "live": the brain against the running sim, in a console, for the step 9 integration checklist. Loads both
/// airports from the sim, runs the brain at 4 Hz, prints every ATC call, and takes pilot calls from number keys.
/// Thrown away once the app has its UI.
/// </summary>
internal static class LiveCommand
{
    public sealed record Args
    {
        public string? From { get; init; }
        public string? To { get; init; }
        public string? Callsign { get; init; }
        public string? SimBriefPilotId { get; init; }
        public string? PlnPath { get; init; }
        public string? Type { get; init; }
        public int? CruiseFl { get; init; }
        public string? Sid { get; init; }
        public string? Star { get; init; }
        public string? Stand { get; init; }
        public string? PushToTalk { get; init; }
        public bool AtcKey { get; init; } = true;
        public bool Arrows { get; init; } = true;
        public double ArrowLookAheadM { get; init; } = 300;
        public double ArrowSpacingM { get; init; } = 10;
        public int Seed { get; init; } = Environment.TickCount;
    }

    public const string Usage =
        "live (--simbrief PILOT_ID | --pln FILE | --from ICAO --to ICAO --callsign VLG1234)\n" +
        "     [--type A320] [--cruise 350] [--sid NAME] [--star NAME] [--stand NAME]   (override the imported plan)\n" +
        "     [--ptt \"VK_LCONTROL+T\" | --ptt \"joystick:0:button:3\"] [--no-atc-key] [--seed N]\n" +
        "     [--no-arrows] [--arrow-lookahead M] [--arrow-spacing M]";

    public static Args? Parse(string[] a)
    {
        string? from = null, to = null, callsign = null, type = null, sid = null, star = null, stand = null, ptt = null, simbrief = null, pln = null;
        int? cruise = null, seed = null;
        bool atcKey = true, arrows = true;
        double lookAhead = 300, spacing = 10;
        for (int i = 1; i < a.Length; i++)
        {
            string? Next() => i + 1 < a.Length ? a[++i] : null;
            switch (a[i])
            {
                case "--simbrief": simbrief = Next(); break;
                case "--pln": pln = Next(); break;
                case "--from": from = Next()?.ToUpperInvariant(); break;
                case "--to": to = Next()?.ToUpperInvariant(); break;
                case "--callsign": callsign = Next()?.ToUpperInvariant(); break;
                case "--type": type = Next(); break;
                case "--cruise": cruise = int.Parse(Next() ?? "", CultureInfo.InvariantCulture); break;
                case "--sid": sid = Next(); break;
                case "--star": star = Next(); break;
                case "--stand": stand = Next(); break;
                case "--ptt": ptt = Next(); break;
                case "--no-atc-key": atcKey = false; break;
                case "--no-arrows": arrows = false; break;
                case "--arrow-lookahead": lookAhead = double.Parse(Next() ?? "", CultureInfo.InvariantCulture); break;
                case "--arrow-spacing": spacing = double.Parse(Next() ?? "", CultureInfo.InvariantCulture); break;
                case "--seed": seed = int.Parse(Next() ?? "", CultureInfo.InvariantCulture); break;
                default: return null;
            }
        }
        if (simbrief is null && pln is null && (from is null || to is null || callsign is null)) return null;
        return new Args
        {
            SimBriefPilotId = simbrief, PlnPath = pln,
            From = from, To = to, Callsign = callsign, Type = type, CruiseFl = cruise, Sid = sid, Star = star, Stand = stand,
            PushToTalk = ptt, AtcKey = atcKey, Seed = seed ?? Environment.TickCount,
            Arrows = arrows, ArrowLookAheadM = lookAhead, ArrowSpacingM = spacing,
        };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Run(Args args)
    {
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };

        string data = DataDirectory();
        var callsigns = CallsignTable.Load(Path.Combine(data, "airline-callsigns.csv"));
        var squawks = DataFiles.ParseSquawks(File.ReadAllText(Path.Combine(data, "squawk-ranges.json")));
        var sops = DataFiles.ParseSops(File.ReadAllText(Path.Combine(data, "airport-sops.json")));
        var ctr = DataFiles.ParseCtrSectors(File.ReadAllText(Path.Combine(data, "ctr-frequencies.json")));

        using var provider = new SimConnectAirportProvider();
        FlightPlan plan;
        PlnFile? pln = null;
        try
        {
            plan = BasePlan(args, callsigns, out pln);
        }
        catch (FlightPlanImportException e)
        {
            Console.Error.WriteLine($"Flight plan import failed ({e.Error}): {e.Message}");
            return 1;
        }

        Console.WriteLine("Waiting for MSFS 2024... (Ctrl+C to quit)");
        var origin = LoadWhenUp(provider, plan.Origin, cancel.Token);
        var destination = origin is null ? null : LoadWhenUp(provider, plan.Destination, cancel.Token);
        if (origin is null || destination is null) return 1;
        if (pln is not null && FromPln(pln, args, callsigns, provider, origin) is { } fromPln) plan = fromPln;
        else if (pln is not null) return 1;
        plan = Overrides(plan, args);
        Console.WriteLine($"Plan: {plan.Callsign}{(plan.Telephony is { } t ? $" ({t})" : "")} {plan.AircraftType} {plan.Origin} {plan.DepartureRunway} {plan.PlannedSid} " +
                          $"-> {plan.Destination} {plan.PlannedStar} {plan.ArrivalRunway}, cruise {plan.CruiseLevel}, {plan.Route.Count} route points" +
                          (plan.Alternate is { } alt ? $", alternate {alt}" : ""));
        Describe(origin);
        Describe(destination);

        Console.WriteLine("Looking for CENTER frequencies along the route (up to a minute)...");
        var simCenters = provider.CenterFrequenciesAlongAsync(CenterSearch.RoutePoints(origin, destination, plan), 100).GetAwaiter().GetResult();
        var centers = CenterSearch.Merge(simCenters, ctr);
        Console.WriteLine($"  {simCenters.Count} from the sim, {ctr.Count} from ctr-frequencies.json, {centers.Count} used");

        var brain = new AtcBrain(new AtcBrainOptions { Squawks = squawks, Sops = sops, CenterFrequencies = centers });
        brain.Start(plan, origin, destination, args.Seed);
        var printer = new LogPrinter(brain.Session, new IcaoRenderer(args.Seed, callsigns: callsigns));
        var gate = new object();
        Console.WriteLine($"Seed {args.Seed}. Brain started: {plan.Callsign} {plan.Origin} -> {plan.Destination}.");

        var bridge = new SimBridge(new SimBridgeOptions { PushToTalkInput = args.PushToTalk, UseAtcKey = args.AtcKey });
        long samples = 0;
        int lastTransmitKhz = 0, lastArrowCount = -1;
        var arrowController = new TaxiArrowController(new TaxiArrowOptions { Enabled = args.Arrows, LookAheadM = args.ArrowLookAheadM, SpacingM = args.ArrowSpacingM });
        bridge.Connected += () => Console.WriteLine("[sim] connected, 4 Hz");
        bridge.Disconnected += () => { if (!cancel.IsCancellationRequested) Console.WriteLine("[sim] connection lost: ATC paused, retrying every 5 s"); };
        bridge.TitleReceived += t => Console.WriteLine($"[sim] aircraft: {t}");
        bridge.Warning += w => Console.WriteLine($"[sim] {w}");
        bridge.PushToTalkChanged += (down, source) => Console.WriteLine($"[ptt] {(down ? "DOWN" : "UP")} ({source})");
        bridge.StateReceived += s =>
        {
            lock (gate)
            {
                brain.Tick(s, s.Time);
                lastTransmitKhz = s.TransmitFrequencyKhz;
                var arrows = arrowController.Update(brain.Session, s);
                bridge.ShowArrows(arrows);
                if ((arrows.Count == 0) != (lastArrowCount == 0))
                    Console.WriteLine(arrows.Count == 0 ? "[arrows] none" : $"[arrows] showing {arrows.Count} along the route");
                lastArrowCount = arrows.Count;
                printer.PrintNew();
                if (++samples % 40 == 0) Console.Title = $"OpenSkyATC live - {brain.Session.Phase} - {s.Time:hh\\:mm\\:ss} - COM1 {s.Com1ActiveKhz / 1000.0:0.000}{(s.IsPaused ? " - paused" : "")}";
            }
        };
        var loop = new Thread(() => bridge.Run(cancel.Token)) { IsBackground = true, Name = "SimBridge" };
        loop.Start();

        Console.WriteLine("Keys: 1-9 send a pilot call from the list, L lists them again, A turns taxi arrows on/off, Q quits.");
        string shown = "";
        while (!cancel.IsCancellationRequested)
        {
            IReadOnlyList<PilotIntent> intents;
            ControllerUnit? expected;
            int tuned;
            lock (gate)
            {
                intents = brain.AvailableIntents.Take(9).ToList();
                expected = brain.ExpectedUnit;
                tuned = lastTransmitKhz;
            }
            string list = string.Join(" | ", intents.Select((x, i) => $"{i + 1} {Describe(x)}"));
            if (expected is not null)
                list += $"   -> call {expected.Callsign} {Mhz(expected.FrequencyKhz)}" +
                        (tuned != 0 && !expected.Answers(tuned) ? $" (you are on {Mhz(tuned)})" : "");
            if (list != shown)
            {
                Console.WriteLine($"[you] {(list.Length == 0 ? "(nothing to say)" : list)}");
                shown = list;
            }

            if (NextKey() is not { } c)
            {
                cancel.Token.WaitHandle.WaitOne(100);
                continue;
            }
            if (c is 'q' or 'Q') break;
            if (c is 'l' or 'L') { shown = ""; continue; }
            if (c is 'a' or 'A')
            {
                lock (gate) arrowController.Options = arrowController.Options with { Enabled = !arrowController.Options.Enabled };
                Console.WriteLine($"[arrows] turned {(arrowController.Options.Enabled ? "on" : "off")}");
                continue;
            }
            int n = c - '1';
            if (n < 0 || n >= intents.Count) continue;
            lock (gate)
            {
                brain.HandlePilotIntent(intents[n]);
                printer.PrintNew();
            }
        }
        bridge.ShowArrows([]);
        Thread.Sleep(500); // let the bridge remove them (the sim would also remove them on disconnect)
        cancel.Cancel();
        loop.Join(TimeSpan.FromSeconds(3));
        Console.WriteLine("Stopped.");
        return 0;
    }

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

    /// <summary>The plan from SimBrief, a .pln (completed once the sim is up) or the command line.</summary>
    private static FlightPlan BasePlan(Args args, CallsignTable callsigns, out PlnFile? pln)
    {
        pln = null;
        if (args.SimBriefPilotId is { } id)
        {
            Console.WriteLine("Fetching your latest SimBrief flight plan (for flight simulation only, not for real-world navigation)...");
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("OpenSkyATC/0.1 (flight simulation ATC; GPL-3.0)");
            return new SimBriefClient(http).FetchLatestAsync(id).GetAwaiter().GetResult();
        }
        if (args.PlnPath is { } path)
        {
            if (!File.Exists(path)) throw new FlightPlanImportException(FlightPlanImportError.NoFlightPlan, $"No such file: {path}");
            pln = PlnParser.Parse(File.ReadAllText(path));
            return new FlightPlan { Callsign = "", Origin = pln.Origin, Destination = pln.Destination };
        }
        return new FlightPlan { Callsign = args.Callsign!, Origin = args.From!, Destination = args.To! };
    }

    /// <summary>A .pln names neither the callsign nor (in MSFS 2024) waypoint positions: both come from the sim.</summary>
    private static FlightPlan? FromPln(PlnFile pln, Args args, CallsignTable callsigns, SimConnectAirportProvider provider, AirportData origin)
    {
        string callsign;
        string? telephony = null;
        if (args.Callsign is { } given) callsign = given;
        else
        {
            var identity = SimAircraftIdentity.Read(TimeSpan.FromSeconds(10));
            if (identity is null || callsigns.FromSimIdentity(identity.Airline, identity.FlightNumber, identity.TailNumber) is not { } cs)
            {
                Console.Error.WriteLine("The .pln has no callsign and the sim aircraft has no airline, flight number or tail number set. Set them in the sim's flight setup, or pass --callsign.");
                return null;
            }
            (callsign, telephony) = cs;
            Console.WriteLine($"Callsign from the sim aircraft: airline '{identity.Airline}', flight '{identity.FlightNumber}', tail '{identity.TailNumber}' -> {callsign}");
        }

        var unknown = pln.Waypoints.Where(w => w.Position is null && w.Kind is 'W' or 'V' or 'N').ToList();
        var found = new Dictionary<PlnWaypoint, GeoPoint>();
        if (unknown.Count > 0)
        {
            Console.WriteLine($"Looking up {unknown.Count} route points in the sim's navdata...");
            var positions = provider.NavaidPositionsAsync(unknown.Select(w => (w.Ident, w.Region, w.Kind)).ToList()).GetAwaiter().GetResult();
            var looseMatches = new HashSet<PlnWaypoint>();
            for (int i = 0; i < unknown.Count; i++)
            {
                if (positions[i] is not { } hit) continue;
                found[unknown[i]] = hit.Position;
                if (hit.WithoutRegion) looseMatches.Add(unknown[i]);
            }
            // A point found only without its region may be another point of the same name: keep it only near the route.
            var previous = origin.ReferencePoint;
            foreach (var w in pln.Waypoints)
            {
                var p = w.Position ?? (found.TryGetValue(w, out var f) ? f : null);
                if (p is null) continue;
                if (looseMatches.Contains(w) && previous is { } prev && GeoMath.DistanceNm(prev, p.Value) > MaxLegNm)
                {
                    found.Remove(w);
                    continue;
                }
                previous = p;
            }
            if (found.Count < unknown.Count)
                Console.WriteLine($"  {unknown.Count - found.Count} not in the sim's navdata (left out): {string.Join(", ", unknown.Where(w => !found.ContainsKey(w)).Select(w => w.Ident))}");
        }
        return pln.ToFlightPlan(callsign, telephony, args.Type, w => found.TryGetValue(w, out var p) ? p : null, origin.TransitionAltitudeFt);
    }

    /// <summary>Longest believable leg between route points (oceanic legs included).</summary>
    private const double MaxLegNm = 1500;

    private static FlightPlan Overrides(FlightPlan plan, Args args) => plan with
    {
        Callsign = args.Callsign ?? plan.Callsign,
        AircraftType = args.Type ?? plan.AircraftType,
        CruiseLevel = args.CruiseFl is { } fl ? AltitudeTarget.FlightLevel(fl) : plan.CruiseLevel,
        PlannedSid = args.Sid ?? plan.PlannedSid,
        PlannedStar = args.Star ?? plan.PlannedStar,
        DestinationStand = args.Stand ?? plan.DestinationStand,
    };

    /// <summary>Retries every 5 s while the sim is not running; null if the sim does not know the airport.</summary>
    private static AirportData? LoadWhenUp(SimConnectAirportProvider provider, string icao, CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                var airport = provider.LoadAsync(icao).GetAwaiter().GetResult();
                if (airport is null) Console.Error.WriteLine($"The sim has no airport {icao}.");
                return airport;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                if (cancel.WaitHandle.WaitOne(TimeSpan.FromSeconds(5))) break;
            }
        }
        return null;
    }

    private static void Describe(AirportData a) =>
        Console.WriteLine($"  {a.Icao} {a.Name}: {a.Runways.Count} runways, TA {a.TransitionAltitudeFt?.ToString(CultureInfo.InvariantCulture) ?? "?"} ft, " +
                          $"{a.Taxi?.Stands.Count ?? 0} stands, units: {string.Join(", ", a.Frequencies.GroupBy(f => f.Role).Select(g => $"{g.First().Callsign} {g.First().FrequencyKhz / 1000.0:0.000}"))}");

    private static string Mhz(int khz) => (khz / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);

    private static string Describe(PilotIntent intent) => intent switch
    {
        Readback r => r.Items.Count == 0 ? "Readback" : "Readback " + string.Join(" ", r.Items.Select(kv => $"{kv.Key}={kv.Value}")),
        CheckIn => "CheckIn",
        _ => intent.GetType().Name,
    };

    /// <summary>Walks up from the executable to the repository's data folder.</summary>
    private static string DataDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "data");
            if (File.Exists(Path.Combine(candidate, "airline-callsigns.csv"))) return candidate;
        }
        throw new DirectoryNotFoundException("Could not find the data folder above " + AppContext.BaseDirectory);
    }

    /// <summary>Prints session log entries as they appear, rendering ATC messages in order.</summary>
    private sealed class LogPrinter(FlightSession session, IcaoRenderer renderer)
    {
        private int _next;

        public void PrintNew()
        {
            var entries = session.Log.Entries;
            for (; _next < entries.Count; _next++)
            {
                var e = entries[_next];
                string? line = e switch
                {
                    AtcTransmissionLogged a => $"ATC  {a.Message.Unit.Callsign} ({a.Message.Unit.FrequencyKhz / 1000.0:0.000}): {renderer.Render(a.Message).Display}",
                    PilotIntentLogged p => $"YOU  ({p.FrequencyKhz / 1000.0:0.000}{(p.Unit is null ? ", nobody on this frequency" : "")}) {Describe(p.Intent)}",
                    PhaseChanged c => $"     phase {c.From} -> {c.To} ({c.Reason})",
                    PhaseInferred i => $"     phase inferred: {i.Phase} ({i.Reason})",
                    ReadbackChecked { Verdict.IsCorrect: true } => null,
                    ReadbackChecked r => $"     readback {r.Tag}: wrong [{string.Join(",", r.Verdict.Wrong)}] missing [{string.Join(",", r.Verdict.Missing)}]",
                    ReadbackMissed m => $"     readback {m.Tag}: missed",
                    RequestRejected r => $"     not now: {Describe(r.Intent)} ({r.Reason})",
                    HandoffSkipped h => $"     skipped {h.Expected.Callsign}, called {h.Called.Callsign}",
                    TaxiFallback t => $"     no taxi route to {t.Destination} at {t.Icao}: {t.Reason}",
                    ConformanceWarning w => $"     conformance: {w.Check} level {w.Level}{(w.Serious ? " (serious)" : "")}",
                    SessionEnded s => $"     session ended: {s.Reason}",
                    _ => null,
                };
                if (line is not null) Console.WriteLine($"{e.Time:hh\\:mm\\:ss} {line}");
            }
        }
    }
}
