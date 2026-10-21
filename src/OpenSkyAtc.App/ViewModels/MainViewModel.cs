using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Host;
using OpenSkyAtc.SimBrief;

namespace OpenSkyAtc.App.ViewModels;

/// <summary>What the main window needs from its view (dialogs), so the view model stays free of WPF windows.</summary>
public interface IMainView
{
    /// <summary>Hard mode: lets the pilot type the readback; null when cancelled.</summary>
    Readback? AskReadback(Readback expected);
    string? PickPlnFile();
    void OpenSettings();
    void ShowError(string title, string message);

    /// <summary>Shows a flight's debrief (step 16).</summary>
    void ShowDebrief(SavedDebrief debrief);
}

/// <param name="Who">"GROUND" for ATC, "You" for the pilot, empty for events.</param>
/// <param name="Auto">A call auto-respond made.</param>
public sealed record TranscriptLine(string Time, string Who, string Text, TranscriptKind Kind, bool Auto = false)
{
    public bool IsAtc => Kind == TranscriptKind.Atc;
    public bool IsPilot => Kind == TranscriptKind.Pilot;
    public bool IsEvent => Kind is TranscriptKind.Event or TranscriptKind.Warning;
}

/// <summary>Which calls the conversation shows (the filter buttons above it).</summary>
public enum TranscriptFilter
{
    All,
    Atc,
    Pilot,
    Events,
}

public sealed partial class IntentButton(PilotIntent intent, string label, bool primary, IRelayCommand<IntentButton> command) : ObservableObject
{
    public bool Danger => Intent is Mayday or PanPan;
    public PilotIntent Intent { get; } = intent;
    public string BaseLabel { get; } = label;

    /// <summary>The label, with the readback countdown ("Read back: 118.080 · 14 s") when one runs.</summary>
    [ObservableProperty] private string _label = label;
    public bool Primary { get; } = primary;

    /// <summary>"1".."9": the key that sends it (main window), or empty after the ninth.</summary>
    public string Number { get; init; } = "";
    public IRelayCommand<IntentButton> Command { get; } = command;
}

/// <summary>
/// The main (and compact) window: one flight at a time through a <see cref="FlightHost"/>. Host events arrive on
/// background threads and are applied on the UI thread, at most a few times a second.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private const int MaxTranscript = 400;

    private readonly Dispatcher _ui;
    private readonly SettingsStore _store;
    private FlightHost? _host;
    private int _refreshQueued;
    private string _intentKey = "";

    public MainViewModel(SettingsStore store, Dispatcher ui)
    {
        TranscriptView = CollectionViewSource.GetDefaultView(Transcript);
        TranscriptView.Filter = Shows;
        _store = store;
        _ui = ui;
        Settings = store.Load();
        Loc.Instance.Language = Settings.Language;
        Loc.Instance.PropertyChanged += (_, _) => Refresh(force: true);
        SendCommand = new RelayCommand<IntentButton>(b => { if (b is not null) Send(b); });
        Refresh(force: true);
    }

    public IMainView? View { get; set; }
    public AppSettings Settings { get; private set; }
    public ObservableCollection<TranscriptLine> Transcript { get; } = [];

    /// <summary>The conversation as shown: <see cref="Transcript"/> through the filter buttons.</summary>
    public ICollectionView TranscriptView { get; }

    [ObservableProperty] private TranscriptFilter _filter;

    partial void OnFilterChanged(TranscriptFilter value) => TranscriptView.Refresh();

    [RelayCommand]
    private void SetFilter(TranscriptFilter filter) => Filter = filter;

    private bool Shows(object item) => item is TranscriptLine l && Filter switch
    {
        TranscriptFilter.Atc => l.IsAtc,
        TranscriptFilter.Pilot => l.IsPilot,
        TranscriptFilter.Events => l.IsEvent,
        _ => true,
    };
    public ObservableCollection<IntentButton> Intents { get; } = [];
    public IRelayCommand<IntentButton> SendCommand { get; }

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string? _statusDetail;
    [ObservableProperty] private bool _connected;
    [ObservableProperty] private bool _pushToTalk;
    [ObservableProperty] private bool _flightActive;
    [ObservableProperty] private string _callsign = "—";
    [ObservableProperty] private string _route = "—";
    [ObservableProperty] private string _phase = "—";
    [ObservableProperty] private string _cleared = "—";
    [ObservableProperty] private string _frequency = "———.———";
    [ObservableProperty] private string _talkingTo = "";
    [ObservableProperty] private string? _hint;
    [ObservableProperty] private bool _hintIsWarning;
    [ObservableProperty] private string? _arrowsText;
    [ObservableProperty] private bool _paused;
    [ObservableProperty] private string? _notice;
    [ObservableProperty] private string _pttText = "";
    [ObservableProperty] private bool _showStatusDetail;

    // The main window's tiles and radio box (refactor phase 5); "—" when there is nothing yet.
    [ObservableProperty] private string _flightLine = "";
    [ObservableProperty] private string _tileRunway = "—";
    [ObservableProperty] private string _tileRoute = "—";
    [ObservableProperty] private string _tileCleared = "—";
    [ObservableProperty] private string _tileSquawk = "—";
    [ObservableProperty] private string _tileNext = "—";
    [ObservableProperty] private string _tileNextName = "";
    [ObservableProperty] private string _tileAtis = "—";
    [ObservableProperty] private string _comLabel = "COM1";
    [ObservableProperty] private string _standby = "";
    [ObservableProperty] private string _stationLine = "";

    /// <summary>Header button: starts a flight with the configured plan source (or ends the current one first).</summary>
    [RelayCommand]
    private async Task ImportPlan()
    {
        if (FlightActive) EndFlight();
        await StartFlight();
    }

    public bool HardMode
    {
        get => Settings.HardMode;
        set
        {
            SaveSettings(Settings with { HardMode = value });
            OnPropertyChanged();
            Refresh(force: true);
        }
    }

    public bool TaxiArrows
    {
        get => Settings.TaxiArrows;
        set
        {
            SaveSettings(Settings with { TaxiArrows = value });
            OnPropertyChanged();
        }
    }

    /// <summary>Answer ATC automatically: readbacks and check-ins (refactor phase 3).</summary>
    public bool AutoRespond
    {
        get => Settings.AutoRespond;
        set
        {
            SaveSettings(Settings with { AutoRespond = value });
            OnPropertyChanged();
        }
    }

    /// <summary>Keep the main window above other windows (replaces the compact strip, refactor phase 5).</summary>
    public bool AlwaysOnTop
    {
        get => Settings.AlwaysOnTop;
        set
        {
            SaveSettings(Settings with { AlwaysOnTop = value });
            OnPropertyChanged();
        }
    }

    /// <summary>Keys 1-9 in the main window: the request with that number.</summary>
    public bool SendNumber(int number)
    {
        if (number < 1 || number > Intents.Count || number > 9) return false;
        Send(Intents[number - 1]);
        return true;
    }

    /// <summary>Tune the next frequency when its change is read back (refactor phase 2).</summary>
    public bool AutoTune
    {
        get => Settings.AutoTune;
        set
        {
            SaveSettings(Settings with { AutoTune = value });
            OnPropertyChanged();
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        Settings = settings;
        _store.Save(settings);
        Loc.Instance.Language = settings.Language;
        _host?.Apply(settings);
        OnPropertyChanged(nameof(HardMode));
        OnPropertyChanged(nameof(TaxiArrows));
        OnPropertyChanged(nameof(AutoTune));
        OnPropertyChanged(nameof(AutoRespond));
        OnPropertyChanged(nameof(AlwaysOnTop));
    }

    [RelayCommand]
    private async Task StartFlight()
    {
        if (FlightActive) return;
        if (Settings.PlanSource == PlanSource.SimBrief && string.IsNullOrWhiteSpace(Settings.SimBriefPilotId))
        {
            View?.ShowError(Loc.Get("Error.Start"), Loc.Get("Error.NeedSimBriefId"));
            View?.OpenSettings();
            return;
        }
        if (Settings.PlanSource == PlanSource.PlnFile && string.IsNullOrWhiteSpace(Settings.LastPlnPath) && !OpenPlnFile()) return;

        Transcript.Clear();
        Notice = null;
        var host = new FlightHost(Settings);
        host.StatusChanged += (_, _) => QueueRefresh();
        host.Updated += QueueRefresh;
        host.Notice += n => _ui.InvokeAsync(() => Notice = n);
        host.Transcript += lines => _ui.InvokeAsync(() => AddTranscript(lines));
        host.DebriefSaved += d => _ui.InvokeAsync(() => View?.ShowDebrief(d));
        _host = host;
        FlightActive = true;
        try
        {
            await Task.Run(() => host.StartAsync(new FlightRequest
            {
                Source = Settings.PlanSource,
                SimBriefPilotId = Settings.SimBriefPilotId,
                PlnPath = Settings.LastPlnPath,
            }));
        }
        catch (Exception e) when (e is FlightPlanImportException or FileNotFoundException or InvalidOperationException or HttpRequestException
                                       or System.Runtime.InteropServices.COMException)
        {
            View?.ShowError(Loc.Get("Error.Start"), e.Message);
            EndFlight();
        }
        Refresh(force: true);
    }

    [RelayCommand]
    private void EndFlight()
    {
        var host = _host;
        _host = null;
        FlightActive = false;
        if (host is not null) Task.Run(host.Dispose);
        Refresh(force: true);
    }

    /// <summary>Asks for a .pln and makes it the plan source.</summary>
    [RelayCommand]
    private void OpenPln() => OpenPlnFile();

    /// <summary>False when cancelled.</summary>
    private bool OpenPlnFile()
    {
        if (View?.PickPlnFile() is not { } path) return false;
        SaveSettings(Settings with { PlanSource = PlanSource.PlnFile, LastPlnPath = path });
        return true;
    }

    [RelayCommand]
    private void OpenSettings() => View?.OpenSettings();

    private void Send(IntentButton button)
    {
        var intent = button.Intent;
        if (intent is Readback r && HardMode && r.Items.Count > 0)
        {
            if (View?.AskReadback(r) is not { } typed) return;
            intent = typed;
        }
        _host?.Send(intent);
    }

    private void AddTranscript(IReadOnlyList<TranscriptEntry> lines)
    {
        foreach (var l in lines)
        {
            string who = l.Kind switch
            {
                TranscriptKind.Atc => l.Station?.Split(' ')[^1] ?? "ATC",
                TranscriptKind.Pilot => Loc.Get("Main.You"),
                _ => "",
            };
            string time = l.Time.TotalHours >= 1 ? l.Time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : l.Time.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
            // The host marks auto calls with " (auto)" in the text (for logs); here the flag shows it instead.
            string text = l.Auto && l.Text.EndsWith(" (auto)", StringComparison.Ordinal) ? l.Text[..^7] : l.Text;
            Transcript.Add(new TranscriptLine(time, l.Kind == TranscriptKind.Atc ? who.ToUpperInvariant() : who, text, l.Kind, l.Auto));
        }
        while (Transcript.Count > MaxTranscript) Transcript.RemoveAt(0);
    }

    private void QueueRefresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1) return;
        _ui.InvokeAsync(() =>
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
            Refresh(force: false);
        }, DispatcherPriority.Background);
    }

    private void Refresh(bool force)
    {
        var s = _host?.Snapshot();
        var status = s?.Status ?? HostStatus.Idle;
        StatusText = Loc.Get($"Status.{status}");
        StatusDetail = s?.StatusDetail ?? (FlightActive ? null : Loc.Get("Main.NoFlight"));
        Connected = s?.SimConnected ?? false;
        PushToTalk = s?.PushToTalk ?? false;
        PttText = Loc.Get(s?.Transcribing == true ? "Status.Transcribing"
            : PushToTalk ? (s?.Listening == true ? "Status.Listening" : "Status.Ptt")
            : "Status.PttReady");
        ShowStatusDetail = !string.IsNullOrEmpty(StatusDetail) && status != HostStatus.Running;
        Paused = s?.Paused ?? false;

        if (s?.Plan is { } plan)
        {
            Callsign = s.CallsignSpoken ?? plan.Callsign;
            Route = $"{plan.Origin} {plan.DepartureRunway} → {plan.Destination}".Replace("  ", " ");
            Phase = Loc.Get($"Phase.{s.Phase}");
            Cleared = string.Join(" · ", new[]
            {
                s.ClearedAltitude is { } a ? (a.Kind == Core.Model.AltitudeKind.FlightLevel ? $"FL{a.Value:000}" : $"{a.Value} ft") : null,
                s.Squawk is { } sq ? Loc.Format("Main.Squawk", sq.ToString("0000", CultureInfo.InvariantCulture)) : null,
            }.Where(x => x is not null));
            if (Cleared.Length == 0) Cleared = "—";
        }
        else if (!FlightActive)
        {
            Callsign = Route = Phase = Cleared = "—";
        }

        Frequency = s is { TunedKhz: > 0 } ? Mhz(s.TunedKhz) : "———.———";
        var expected = s?.Expected;
        TalkingTo = expected is not null && s!.TunedKhz != 0 && expected.Answers(s.TunedKhz) ? expected.Callsign : "";
        ComLabel = $"COM{s?.Com ?? 1}";
        Standby = s is { StandbyKhz: > 0 } ? Mhz(s.StandbyKhz) : "";
        StationLine = TalkingTo.Length == 0 ? Loc.Get("Main.NoStation") : s?.Runway is { } rwy ? $"{TalkingTo} · {rwy}" : TalkingTo;
        FlightLine = s?.Plan is null ? Loc.Get("Main.NoFlightShort") : $"{Callsign} · {Route} · {Phase}";
        TileRunway = s?.Runway ?? "—";
        TileRoute = s?.Sid ?? (s?.ClearedAltitude is not null ? Loc.Get("Main.FlightPlanned") : "—");
        TileCleared = s?.ClearedAltitude is { } alt ? (alt.Kind == Core.Model.AltitudeKind.FlightLevel ? $"FL{alt.Value:000}" : $"{alt.Value} ft") : "—";
        TileSquawk = s?.Squawk is { } code ? code.ToString("0000", CultureInfo.InvariantCulture) : "—";
        var nextUnit = s?.HandoffTo ?? (expected is not null && s!.TunedKhz != 0 && !expected.Answers(s.TunedKhz) ? expected : null);
        TileNext = nextUnit is not null ? Mhz(nextUnit.FrequencyKhz) : "—";
        TileNextName = nextUnit?.Callsign ?? "";
        TileAtis = (s?.AtisLetter, s?.Qnh) switch
        {
            ({ } letter, { } q) => $"{letter} · {q}",
            (null, { } q) => q.ToString(CultureInfo.InvariantCulture),
            ({ } letter, null) => letter.ToString(),
            _ => "—",
        };
        if (s?.HandoffTo is { } next)
        {
            Hint = Loc.Format("Main.Next", next.Callsign, Mhz(next.FrequencyKhz));
            HintIsWarning = true;
        }
        else if (expected is not null && s!.TunedKhz != 0 && !expected.Answers(s.TunedKhz))
        {
            Hint = Loc.Format("Main.Tune", expected.Callsign, Mhz(expected.FrequencyKhz), Mhz(s.TunedKhz));
            HintIsWarning = true;
        }
        else if (s is { ReleaseBrakeToPush: true })
        {
            Hint = Loc.Get("Main.ReleaseBrake");
            HintIsWarning = false;
        }
        else
        {
            Hint = null;
            HintIsWarning = false;
        }
        ArrowsText = s is { Arrows: > 0 } ? Loc.Format("Main.Arrows", s.Arrows, Settings.ArrowLookAheadM.ToString("0", CultureInfo.InvariantCulture)) : null;

        var intents = s?.Intents ?? [];
        string? countdown = s?.ReadbackTimeLeft is { } left ? $" · {Math.Ceiling(left.TotalSeconds):0} s" : null;
        string key = string.Join("|", intents.Select(i => i.ToString())) + HardMode + Loc.Instance.Language;
        if (force || key != _intentKey)
        {
            _intentKey = key;
            Intents.Clear();
            int n = 0;
            foreach (var i in intents)
            {
                n++;
                Intents.Add(new IntentButton(i, Label(i), i is Readback, SendCommand) { Number = n <= 9 ? n.ToString(CultureInfo.InvariantCulture) : "" });
            }
        }
        foreach (var b in Intents)
            b.Label = b.Intent is Readback { Items.Count: > 0 } ? b.BaseLabel + countdown : b.BaseLabel;

    }

    private string Label(PilotIntent intent) => intent switch
    {
        Readback { Items.Count: 0 } => Loc.Get("Intent.Readback"),
        Readback when HardMode => Loc.Get("Intent.ReadbackForm"),
        Readback r => $"{Loc.Get("Intent.Readback")}: {PilotPhrase.Of(r)}",
        RequestLevel l => Loc.Format("Intent.RequestLevelTo", l.Level.Kind == Core.Model.AltitudeKind.FlightLevel ? $"FL{l.Level.Value:000}" : $"{l.Level.Value} ft"),
        RequestDirect d => Loc.Format("Intent.RequestDirectTo", d.Fix),
        RequestRunway r => Loc.Format("Intent.RequestRunwayTo", r.Runway),
        _ => Loc.Get($"Intent.{intent.GetType().Name}"),
    };

    private static string Mhz(int khz) => (khz / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);

    /// <summary>Sample content for the developer screenshot (App --screenshot); never used in a flight.</summary>
    internal void ShowSample()
    {
        StatusText = Loc.Get("Status.Running");
        StatusDetail = "IBE1234 LEMD to LEBL";
        ShowStatusDetail = false;
        Connected = true;
        FlightActive = true;
        Callsign = "Iberia 1234";
        Route = "LEMD 36L → LEBL";
        Phase = Loc.Get("Phase.TaxiOut");
        Cleared = "5000 ft · sq 4774";
        Frequency = "121.630";
        TalkingTo = "Barajas Ground";
        Hint = Loc.Format("Main.Next", "Barajas Tower", "118.080");
        HintIsWarning = true;
        ArrowsText = Loc.Format("Main.Arrows", 30, 300);
        FlightLine = $"{Callsign} · {Route} · {Phase}";
        TileRunway = "36L";
        TileRoute = "PINA4N";
        TileCleared = "5000 ft";
        TileSquawk = "4774";
        TileNext = "118.080";
        TileNextName = "Barajas Tower";
        TileAtis = "B · 1013";
        ComLabel = "COM1";
        Standby = "130.080";
        StationLine = "Barajas Ground · 36L";
        AddTranscript(
        [
            new(TimeSpan.FromSeconds(25), TranscriptKind.Pilot, "Barajas Delivery", 130_080, "Barajas Delivery, Iberia 1234, request IFR clearance"),
            new(TimeSpan.FromSeconds(27), TranscriptKind.Atc, "Barajas Delivery", 130_080, "Iberia 1234, Barajas Delivery, cleared to LEBL via flight planned route, climb to altitude 5000 ft, squawk 4774"),
            new(TimeSpan.FromSeconds(30), TranscriptKind.Event, null, null, "Phase: cleared"),
            new(TimeSpan.FromSeconds(68), TranscriptKind.Atc, "Barajas Ground", 121_630, "Iberia 1234, taxi to holding point runway 36L via EB, A, L"),
            new(TimeSpan.FromSeconds(72), TranscriptKind.Pilot, "Barajas Ground", 121_630, "Runway 36L via EB, A, L, Iberia 1234") { Auto = true },
            new(TimeSpan.FromSeconds(190), TranscriptKind.Warning, null, null, "Off the cleared taxi route"),
            new(TimeSpan.FromSeconds(220), TranscriptKind.Atc, "Barajas Ground", 121_630, "Iberia 1234, contact Tower 118.080, bye bye"),
        ]);
        Intents.Clear();
        Intents.Add(new IntentButton(new Readback(new Dictionary<ReadbackItem, string> { [ReadbackItem.Frequency] = "118080" }), $"{Loc.Get("Intent.Readback")}: 118.080 · 14 s", true, SendCommand) { Number = "1" });
        Intents.Add(new IntentButton(new ReadyForDeparture(), Loc.Get("Intent.ReadyForDeparture"), false, SendCommand) { Number = "2" });
        Intents.Add(new IntentButton(new RequestRunway("27L"), Label(new RequestRunway("27L")), false, SendCommand) { Number = "3" });
        Intents.Add(new IntentButton(new RequestWind(), Label(new RequestWind()), false, SendCommand) { Number = "4" });
        Intents.Add(new IntentButton(new SayAgain(), Loc.Get("Intent.SayAgain"), false, SendCommand) { Number = "5" });
        Intents.Add(new IntentButton(new RadioCheck(), Label(new RadioCheck()), false, SendCommand) { Number = "6" });
        Intents.Add(new IntentButton(new Mayday(), Loc.Get("Intent.Mayday"), false, SendCommand) { Number = "7" });
    }

    public void Dispose() => _host?.Dispose();
}
