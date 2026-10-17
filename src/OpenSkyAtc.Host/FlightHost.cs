using System.Runtime.InteropServices;
using OpenSkyAtc.Core.Airspace;
using OpenSkyAtc.Core.Brain;
using OpenSkyAtc.Core.Config;
using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Sim;
using OpenSkyAtc.Phraseology;
using OpenSkyAtc.SimBrief;
using OpenSkyAtc.SimConnect;
using OpenSkyAtc.TaxiArrows;
using OpenSkyAtc.Voice;

namespace OpenSkyAtc.Host;

public enum HostStatus
{
    Idle,
    ImportingPlan,
    WaitingForSim,
    LoadingAirports,
    SearchingCenters,
    DownloadingVoices,
    Running,
    SimDisconnected,
    Failed,
    Stopped,
}

/// <summary>What to fly: the plan source plus optional overrides (the console's command-line options).</summary>
public sealed record FlightRequest
{
    public PlanSource Source { get; init; }
    public string? SimBriefPilotId { get; init; }
    public string? PlnPath { get; init; }

    /// <summary>A plan without SimBrief or a file (console only).</summary>
    public string? From { get; init; }
    public string? To { get; init; }

    public string? Callsign { get; init; }
    public string? AircraftType { get; init; }
    public AltitudeTarget? Cruise { get; init; }
    public string? Sid { get; init; }
    public string? Star { get; init; }
    public string? Stand { get; init; }
    public int? Seed { get; init; }
}

/// <summary>A consistent picture of the flight for the UI, taken under the brain's lock.</summary>
public sealed record HostSnapshot
{
    public HostStatus Status { get; init; }
    public string? StatusDetail { get; init; }
    public bool SimConnected { get; init; }
    public bool PushToTalk { get; init; }
    public FlightPlan? Plan { get; init; }
    public string? CallsignSpoken { get; init; }
    public FlightPhase Phase { get; init; }
    public AltitudeTarget? ClearedAltitude { get; init; }
    public int? Squawk { get; init; }
    public string? Runway { get; init; }
    public int TunedKhz { get; init; }
    public ControllerUnit? Expected { get; init; }
    public ControllerUnit? HandoffTo { get; init; }
    public IReadOnlyList<PilotIntent> Intents { get; init; } = [];
    public int Arrows { get; init; }
    public bool Paused { get; init; }
    public TimeSpan SimTime { get; init; }
}

/// <summary>
/// Runs one flight against the live sim (steps 9-13): imports the plan, waits for MSFS, loads both airports from it,
/// finds CENTER frequencies, starts the brain, ATC voices and taxi arrows, and drives them at 4 Hz on the bridge
/// thread. Thread-safe: UI threads call <see cref="Send"/>, <see cref="Snapshot"/> and <see cref="Apply"/>; events
/// are raised on background threads (marshal them to the UI).
/// </summary>
public sealed class FlightHost : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cancel = new();
    private AppSettings _settings;
    private AtcBrain? _brain;
    private TranscriptWriter? _transcript;
    private SimBridge? _bridge;
    private AtcRadio? _radio;
    private SimConnectAirportProvider? _provider;
    private Thread? _loop;
    private readonly TaxiArrowController _arrows = new();
    private SimState? _state;
    private int _arrowCount;
    private HostStatus _status = HostStatus.Idle;
    private string? _statusDetail;
    private string? _spokenCallsign;

    public FlightHost(AppSettings settings)
    {
        _settings = settings;
        _arrows.Options = ArrowOptions(settings);
    }

    /// <summary>Status changes (import, waiting for the sim, running, disconnected...) with a detail line for the user.</summary>
    public event Action<HostStatus, string?>? StatusChanged;

    /// <summary>New transcript lines, in order.</summary>
    public event Action<IReadOnlyList<TranscriptEntry>>? Transcript;

    /// <summary>The snapshot may have changed (raised at most 4 times a second, and after each pilot call).</summary>
    public event Action? Updated;

    /// <summary>Things the user should know that are not part of the radio (voice download, arrows package missing...).</summary>
    public event Action<string>? Notice;

    public HostStatus Status => _status;
    public DataBundle Data { get; } = DataBundle.Load();

    public HostSnapshot Snapshot()
    {
        lock (_gate)
        {
            var c = _brain?.Session.Clearance;
            return new HostSnapshot
            {
                Status = _status,
                StatusDetail = _statusDetail,
                SimConnected = _bridge?.IsConnected ?? false,
                PushToTalk = _bridge?.PushToTalkHeld ?? false,
                Plan = _brain is null ? null : _plan,
                CallsignSpoken = _spokenCallsign,
                Phase = _brain?.Session.Phase ?? FlightPhase.Preflight,
                ClearedAltitude = c?.ClearedAltitude,
                Squawk = c?.Squawk,
                Runway = c?.Runway,
                TunedKhz = _state?.TransmitFrequencyKhz ?? 0,
                Expected = _brain?.ExpectedUnit,
                HandoffTo = c?.HandoffTo,
                Intents = _brain?.AvailableIntents ?? [],
                Arrows = _arrowCount,
                Paused = _state?.IsPaused ?? false,
                SimTime = _brain?.Session.SimTime ?? TimeSpan.Zero,
            };
        }
    }

    private FlightPlan? _plan;

    /// <summary>"Iberia 1234" for IBE1234 (telephony from the plan or the airline table), else the code itself.</summary>
    private string SpokenCallsign(FlightPlan plan)
    {
        string code = plan.Callsign;
        string? telephony = plan.Telephony ?? (code.Length > 3 ? Data.Callsigns.Telephony(code[..3]) : null);
        return telephony is not null && code.Length > 3 && char.IsAsciiDigit(code[3]) ? $"{telephony} {code[3..]}" : code;
    }

    /// <summary>Imports the plan, waits for the sim and starts the flight. Throws <see cref="FlightPlanImportException"/> for plan problems.</summary>
    public async Task StartAsync(FlightRequest request)
    {
        var cancel = _cancel.Token;
        try
        {
            SimConnectLibrary.EnsureLoaded();
            _provider = new SimConnectAirportProvider();

            SetStatus(HostStatus.ImportingPlan, request.Source == PlanSource.SimBrief ? "Fetching your latest SimBrief plan" : "Reading the flight plan");
            var (plan, pln) = await BasePlanAsync(request);

            SetStatus(HostStatus.WaitingForSim, "Waiting for MSFS 2024");
            var origin = await LoadWhenUpAsync(plan.Origin, cancel);
            SetStatus(HostStatus.LoadingAirports, $"Loading {plan.Origin} and {plan.Destination} from the sim");
            var destination = await LoadWhenUpAsync(plan.Destination, cancel);
            if (pln is not null) plan = await FromPlnAsync(pln, request, origin);
            plan = Overrides(plan, request);

            SetStatus(HostStatus.SearchingCenters, "Looking for CENTER frequencies along the route");
            var simCenters = await _provider.CenterFrequenciesAlongAsync(CenterSearch.RoutePoints(origin, destination, plan), 100);
            var centers = CenterSearch.Merge(simCenters, Data.CtrSectors);

            int seed = request.Seed ?? Environment.TickCount;
            var brain = new AtcBrain(new AtcBrainOptions { Squawks = Data.Squawks, Sops = Data.Sops, CenterFrequencies = centers });
            brain.Start(plan, origin, destination, seed);
            lock (_gate)
            {
                _brain = brain;
                _plan = plan;
                _spokenCallsign = SpokenCallsign(plan);
                _transcript = new TranscriptWriter(brain.Session, new IcaoRenderer(seed, callsigns: Data.Callsigns));
            }

            if (_settings.VoiceEnabled) _radio = await StartRadioAsync(seed, brain);
            StartBridge();
            SetStatus(HostStatus.Running, $"{plan.Callsign} {plan.Origin} to {plan.Destination}");
        }
        catch (OperationCanceledException)
        {
            SetStatus(HostStatus.Stopped, null);
        }
        catch (FlightPlanImportException e)
        {
            SetStatus(HostStatus.Failed, e.Message);
            throw;
        }
        catch (Exception e) when (e is FileNotFoundException or COMException or InvalidOperationException or HttpRequestException)
        {
            SetStatus(HostStatus.Failed, e.Message);
            throw;
        }
    }

    /// <summary>Sends a pilot call (button, readback form, later speech).</summary>
    public void Send(PilotIntent intent)
    {
        IReadOnlyList<TranscriptEntry> lines;
        lock (_gate)
        {
            if (_brain is null) return;
            _brain.HandlePilotIntent(intent);
            lines = _transcript!.TakeNew();
        }
        if (lines.Count > 0) Transcript?.Invoke(lines);
        Updated?.Invoke();
    }

    /// <summary>Applies changed settings at once (arrows, volume); voices, push-to-talk and the plan apply to the next flight.</summary>
    public void Apply(AppSettings settings)
    {
        lock (_gate)
        {
            _settings = settings;
            _arrows.Options = ArrowOptions(settings);
            if (_radio is not null) _radio.Volume = Math.Clamp(settings.Volume, 0f, 1f);
        }
        Updated?.Invoke();
    }

    private static TaxiArrowOptions ArrowOptions(AppSettings s) =>
        new() { Enabled = s.TaxiArrows, LookAheadM = Math.Clamp(s.ArrowLookAheadM, 50, 1000), SpacingM = Math.Clamp(s.ArrowSpacingM, 3, 50) };

    private void StartBridge()
    {
        var bridge = new SimBridge(new SimBridgeOptions { PushToTalkInput = _settings.PushToTalkInput, UseAtcKey = _settings.UseAtcKey });
        _bridge = bridge;
        int ticks = 0;
        bridge.Connected += () => { if (_status == HostStatus.SimDisconnected) SetStatus(HostStatus.Running, "Reconnected to MSFS"); };
        bridge.Disconnected += () =>
        {
            _radio?.Silence();
            if (!_cancel.IsCancellationRequested) SetStatus(HostStatus.SimDisconnected, "Connection to MSFS lost: ATC paused, retrying every 5 s");
        };
        bridge.Warning += w => Notice?.Invoke(w);
        bridge.PushToTalkChanged += (_, _) => Updated?.Invoke();
        bridge.StateReceived += s =>
        {
            IReadOnlyList<TranscriptEntry> lines;
            lock (_gate)
            {
                _state = s;
                _brain!.Tick(s, s.Time);
                _radio?.Update(bridge.PushToTalkHeld, khz => s.IsTunedTo(khz));
                var arrows = _arrows.Update(_brain.Session, s);
                bridge.ShowArrows(arrows);
                _arrowCount = arrows.Count;
                lines = _transcript!.TakeNew();
            }
            if (lines.Count > 0) Transcript?.Invoke(lines);
            if (lines.Count > 0 || ++ticks % 2 == 0) Updated?.Invoke();
        };
        _loop = new Thread(() => bridge.Run(_cancel.Token)) { IsBackground = true, Name = "SimBridge" };
        _loop.Start();
    }

    private async Task<AtcRadio?> StartRadioAsync(int seed, AtcBrain brain)
    {
        var store = new VoiceStore();
        var wanted = VoiceCatalog.All.Where(m => _settings.VoiceModels.Contains(m.Id, StringComparer.OrdinalIgnoreCase)).ToList();
        if (wanted.Count == 0) wanted = [.. VoiceCatalog.All];
        var missing = wanted.Where(m => !store.IsInstalled(m)).ToList();
        if (missing.Count > 0)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            foreach (var m in missing)
            {
                SetStatus(HostStatus.DownloadingVoices, $"Downloading the {m.Id} ATC voice (once, about 70 MB)");
                try
                {
                    await store.InstallAsync(m, http, cancel: _cancel.Token);
                }
                catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
                {
                    Notice?.Invoke($"Could not download the {m.Id} voice: {e.Message}");
                }
            }
        }
        var installed = wanted.Where(store.IsInstalled).ToList();
        if (installed.Count == 0)
        {
            Notice?.Invoke("No ATC voice is installed: text only.");
            return null;
        }
        var radio = new AtcRadio(new IcaoRenderer(seed, callsigns: Data.Callsigns), new VoiceAssigner(seed, installed), new PiperSynthesizer(store), seed)
        {
            Volume = Math.Clamp(_settings.Volume, 0f, 1f),
            Filter = _settings.RadioFilter,
        };
        radio.Error += e => Notice?.Invoke(e);
        radio.Action += a =>
        {
            if (a is Dropped d) Notice?.Invoke($"You left {d.Transmission.Unit} before it spoke.");
        };
        brain.Transmission += m => radio.Say(m);
        radio.Heard += brain.TransmissionHeard; // raised from Update, inside the brain's lock
        return radio;
    }

    private async Task<(FlightPlan Plan, PlnFile? Pln)> BasePlanAsync(FlightRequest r)
    {
        if (r.From is { } from && r.To is { } to && r.Callsign is { } cs)
            return (new FlightPlan { Callsign = cs, Origin = from, Destination = to }, null);
        if (r.Source == PlanSource.SimBrief)
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("OpenSkyATC/0.1 (flight simulation ATC; GPL-3.0)");
            return (await new SimBriefClient(http).FetchLatestAsync(r.SimBriefPilotId, _cancel.Token), null);
        }
        if (r.PlnPath is not { } path || !File.Exists(path))
            throw new FlightPlanImportException(FlightPlanImportError.NoFlightPlan, $"Choose a .pln flight plan file{(r.PlnPath is null ? "" : $" ({r.PlnPath} does not exist)")}.");
        var pln = PlnParser.Parse(await File.ReadAllTextAsync(path));
        return (new FlightPlan { Callsign = "", Origin = pln.Origin, Destination = pln.Destination }, pln);
    }

    /// <summary>Longest believable leg between route points (oceanic legs included).</summary>
    private const double MaxLegNm = 1500;

    /// <summary>A .pln names neither the callsign nor (in MSFS 2024) waypoint positions: both come from the sim.</summary>
    private async Task<FlightPlan> FromPlnAsync(PlnFile pln, FlightRequest request, AirportData origin)
    {
        string callsign;
        string? telephony = null;
        if (request.Callsign is { } given) callsign = given;
        else
        {
            var identity = await Task.Run(() => SimAircraftIdentity.Read(TimeSpan.FromSeconds(10)));
            if (identity is null || Data.Callsigns.FromSimIdentity(identity.Airline, identity.FlightNumber, identity.TailNumber) is not { } cs)
                throw new FlightPlanImportException(FlightPlanImportError.BadResponse,
                    "The .pln has no callsign, and the sim aircraft has no airline, flight number or tail number. Set them in the sim's flight setup.");
            (callsign, telephony) = cs;
        }

        var unknown = pln.Waypoints.Where(w => w.Position is null && w.Kind is 'W' or 'V' or 'N').ToList();
        var found = new Dictionary<PlnWaypoint, GeoPoint>();
        if (unknown.Count > 0)
        {
            SetStatus(HostStatus.LoadingAirports, $"Looking up {unknown.Count} route points in the sim's navdata");
            var positions = await _provider!.NavaidPositionsAsync(unknown.Select(w => (w.Ident, w.Region, w.Kind)).ToList());
            var loose = new HashSet<PlnWaypoint>();
            for (int i = 0; i < unknown.Count; i++)
            {
                if (positions[i] is not { } hit) continue;
                found[unknown[i]] = hit.Position;
                if (hit.WithoutRegion) loose.Add(unknown[i]);
            }
            // A point found only without its region may be another point of the same name: keep it only near the route.
            var previous = origin.ReferencePoint;
            foreach (var w in pln.Waypoints)
            {
                var p = w.Position ?? (found.TryGetValue(w, out var f) ? f : null);
                if (p is null) continue;
                if (loose.Contains(w) && previous is { } prev && GeoMath.DistanceNm(prev, p.Value) > MaxLegNm)
                {
                    found.Remove(w);
                    continue;
                }
                previous = p;
            }
            int left = unknown.Count(w => !found.ContainsKey(w));
            if (left > 0) Notice?.Invoke($"{left} route point(s) are not in the sim's navdata and were left out.");
        }
        return pln.ToFlightPlan(callsign, telephony, request.AircraftType, w => found.TryGetValue(w, out var p) ? p : null, origin.TransitionAltitudeFt);
    }

    private static FlightPlan Overrides(FlightPlan plan, FlightRequest r) => plan with
    {
        Callsign = r.Callsign ?? plan.Callsign,
        AircraftType = r.AircraftType ?? plan.AircraftType,
        CruiseLevel = r.Cruise ?? plan.CruiseLevel,
        PlannedSid = r.Sid ?? plan.PlannedSid,
        PlannedStar = r.Star ?? plan.PlannedStar,
        DestinationStand = r.Stand ?? plan.DestinationStand,
    };

    /// <summary>Retries every 5 s while the sim is not running.</summary>
    private async Task<AirportData> LoadWhenUpAsync(string icao, CancellationToken cancel)
    {
        while (true)
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                return await _provider!.LoadAsync(icao) ?? throw new InvalidOperationException($"The sim has no airport {icao}.");
            }
            catch (COMException)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancel);
            }
        }
    }

    private void SetStatus(HostStatus status, string? detail)
    {
        lock (_gate)
        {
            _status = status;
            _statusDetail = detail;
        }
        StatusChanged?.Invoke(status, detail);
        Updated?.Invoke();
    }

    /// <summary>Ends the flight: removes the arrows, stops the voice and the bridge.</summary>
    public void Stop()
    {
        if (_cancel.IsCancellationRequested) return;
        _bridge?.ShowArrows([]);
        Thread.Sleep(300); // let the bridge remove the arrows (the sim also removes them on disconnect)
        _cancel.Cancel();
        _radio?.Silence();
        _loop?.Join(TimeSpan.FromSeconds(3));
        SetStatus(HostStatus.Stopped, null);
    }

    public void Dispose()
    {
        Stop();
        _radio?.Dispose();
        _provider?.Dispose();
        _cancel.Dispose();
    }
}

/// <summary>The owner-filled data files and the airline table, from the app's data folder.</summary>
public sealed record DataBundle(CallsignTable Callsigns, Core.Planning.SquawkConfig Squawks, Core.Planning.SopConfig Sops, IReadOnlyList<CenterFrequency> CtrSectors)
{
    public static DataBundle Load(string? folder = null)
    {
        string data = folder ?? Find();
        string Text(string name) => File.ReadAllText(Path.Combine(data, name));
        return new DataBundle(
            CallsignTable.Load(Path.Combine(data, "airline-callsigns.csv")),
            DataFiles.ParseSquawks(Text("squawk-ranges.json")),
            DataFiles.ParseSops(Text("airport-sops.json")),
            DataFiles.ParseCtrSectors(Text("ctr-frequencies.json")));
    }

    /// <summary>The data folder next to the app, else the repository's (when run from a build folder).</summary>
    private static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "data");
            if (File.Exists(Path.Combine(candidate, "airline-callsigns.csv"))) return candidate;
        }
        throw new DirectoryNotFoundException("Could not find the data folder above " + AppContext.BaseDirectory);
    }
}
