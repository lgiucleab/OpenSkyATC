using System.Collections.ObjectModel;
using System.Globalization;
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

public sealed record TranscriptLine(string Time, string Who, string Text, TranscriptKind Kind);

public sealed partial class IntentButton(PilotIntent intent, string label, bool primary, IRelayCommand<IntentButton> command) : ObservableObject
{
    public bool Danger => Intent is Mayday or PanPan;
    public PilotIntent Intent { get; } = intent;
    public string BaseLabel { get; } = label;

    /// <summary>The label, with the readback countdown ("Read back: 118.080 · 14 s") when one runs.</summary>
    [ObservableProperty] private string _label = label;
    public bool Primary { get; } = primary;
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
    [ObservableProperty] private string? _lastAtc;
    [ObservableProperty] private bool _paused;
    [ObservableProperty] private string? _notice;
    [ObservableProperty] private string _pttText = "";
    [ObservableProperty] private bool _showStatusDetail;

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
            Transcript.Add(new TranscriptLine(time, who, l.Text, l.Kind));
            if (l.Kind == TranscriptKind.Atc) LastAtc = $"{l.Station}: {l.Text}";
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
            foreach (var i in intents) Intents.Add(new IntentButton(i, Label(i), i is Readback, SendCommand));
        }
        foreach (var b in Intents)
            b.Label = b.Intent is Readback { Items.Count: > 0 } ? b.BaseLabel + countdown : b.BaseLabel;

    }

    private string Label(PilotIntent intent) => intent switch
    {
        Readback { Items.Count: 0 } => Loc.Get("Intent.Readback"),
        Readback when HardMode => Loc.Get("Intent.ReadbackForm"),
        Readback r => $"{Loc.Get("Intent.Readback")}: {PilotPhrase.Of(r)}",
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
        LastAtc = "Barajas Ground: Iberia 1234, contact Tower 118.080, bye bye";
        AddTranscript(
        [
            new(TimeSpan.FromSeconds(25), TranscriptKind.Pilot, "Barajas Delivery", 130_080, "Barajas Delivery, Iberia 1234, request IFR clearance"),
            new(TimeSpan.FromSeconds(27), TranscriptKind.Atc, "Barajas Delivery", 130_080, "Iberia 1234, Barajas Delivery, cleared to LEBL via flight planned route, climb to altitude 5000 ft, squawk 4774"),
            new(TimeSpan.FromSeconds(30), TranscriptKind.Event, null, null, "Phase: cleared"),
            new(TimeSpan.FromSeconds(68), TranscriptKind.Atc, "Barajas Ground", 121_630, "Iberia 1234, taxi to holding point runway 36L via EB, A, L"),
            new(TimeSpan.FromSeconds(72), TranscriptKind.Pilot, "Barajas Ground", 121_630, "Runway 36L via EB, A, L, Iberia 1234"),
            new(TimeSpan.FromSeconds(190), TranscriptKind.Warning, null, null, "Off the cleared taxi route"),
            new(TimeSpan.FromSeconds(220), TranscriptKind.Atc, "Barajas Ground", 121_630, "Iberia 1234, contact Tower 118.080, bye bye"),
        ]);
        Intents.Clear();
        Intents.Add(new IntentButton(new Readback(new Dictionary<ReadbackItem, string> { [ReadbackItem.Frequency] = "118080" }), $"{Loc.Get("Intent.Readback")}: 118.080", true, SendCommand));
        Intents.Add(new IntentButton(new ReadyForDeparture(), Loc.Get("Intent.ReadyForDeparture"), false, SendCommand));
        Intents.Add(new IntentButton(new SayAgain(), Loc.Get("Intent.SayAgain"), false, SendCommand));
        Intents.Add(new IntentButton(new Mayday(), Loc.Get("Intent.Mayday"), false, SendCommand));
    }

    public void Dispose() => _host?.Dispose();
}
