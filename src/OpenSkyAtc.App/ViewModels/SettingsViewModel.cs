using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenSkyAtc.Host;
using OpenSkyAtc.Voice;

namespace OpenSkyAtc.App.ViewModels;

public sealed partial class VoiceChoice(VoiceModel model, bool enabled, bool installed) : ObservableObject
{
    public VoiceModel Model { get; } = model;
    public string Title => $"{Model.Id} ({Model.Speakers.Count} · {Model.Licence})";
    public bool Installed { get; } = installed;
    [ObservableProperty] private bool _enabled = enabled;
}

public sealed record LanguageChoice(string Code, string Name);

/// <summary>An editable copy of the settings; <see cref="ToSettings"/> builds the result when the user saves.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _original;

    public SettingsViewModel(AppSettings s)
    {
        _original = s;
        _useSimBrief = s.PlanSource == PlanSource.SimBrief;
        _simBriefPilotId = s.SimBriefPilotId ?? "";
        _plnPath = s.LastPlnPath ?? "";
        _voiceEnabled = s.VoiceEnabled;
        _volume = s.Volume * 100;
        _radioFilter = s.RadioFilter;
        _pushToTalkInput = s.PushToTalkInput ?? "";
        _useAtcKey = s.UseAtcKey;
        _keyboardPushToTalk = s.KeyboardPushToTalk ?? "";
        _voiceInput = s.VoiceInput;
        _whisperModel = s.WhisperModel;
        Microphones = [new(-1, Loc.Get("Settings.DefaultMicrophone")), .. Voice.Input.Microphone.Devices().Select(d => new MicrophoneChoice(d.Number, d.Name))];
        _microphone = Microphones.FirstOrDefault(m => m.Number == s.MicrophoneDevice) ?? Microphones[0];
        _taxiArrows = s.TaxiArrows;
        _recordFlights = s.RecordFlights;
        _recordVoiceClips = s.RecordVoiceClips;
        _arrowLookAheadM = s.ArrowLookAheadM;
        _arrowSpacingM = s.ArrowSpacingM;
        _language = Languages.FirstOrDefault(l => l.Code == s.Language) ?? Languages[0];
        var store = new VoiceStore();
        Voices = VoiceCatalog.All.Select(m => new VoiceChoice(m, s.VoiceModels.Contains(m.Id, StringComparer.OrdinalIgnoreCase), store.IsInstalled(m))).ToList();
    }

    public IReadOnlyList<VoiceChoice> Voices { get; }
    public IReadOnlyList<LanguageChoice> Languages { get; } = [new("en", "English"), new("es", "Español")];
    public IReadOnlyList<string> WhisperModels { get; } = Voice.Input.WhisperModels.All.Select(m => m.Id).ToList();
    public IReadOnlyList<MicrophoneChoice> Microphones { get; }

    [ObservableProperty] private bool _useSimBrief;
    [ObservableProperty] private string _simBriefPilotId;
    [ObservableProperty] private string _plnPath;
    [ObservableProperty] private bool _voiceEnabled;
    [ObservableProperty] private double _volume;
    [ObservableProperty] private bool _radioFilter;
    [ObservableProperty] private string _pushToTalkInput;
    [ObservableProperty] private bool _useAtcKey;
    [ObservableProperty] private string _keyboardPushToTalk;
    [ObservableProperty] private bool _voiceInput;
    [ObservableProperty] private MicrophoneChoice _microphone;
    [ObservableProperty] private string _whisperModel;
    [ObservableProperty] private bool _taxiArrows;
    [ObservableProperty] private bool _recordFlights;

    [ObservableProperty]
    private bool _recordVoiceClips;
    [ObservableProperty] private double _arrowLookAheadM;
    [ObservableProperty] private double _arrowSpacingM;
    [ObservableProperty] private LanguageChoice _language;
    [ObservableProperty] private string? _testStatus;

    public bool UsePln
    {
        get => !UseSimBrief;
        set => UseSimBrief = !value;
    }

    partial void OnUseSimBriefChanged(bool value) => OnPropertyChanged(nameof(UsePln));

    public AppSettings ToSettings() => _original with
    {
        PlanSource = UseSimBrief ? PlanSource.SimBrief : PlanSource.PlnFile,
        SimBriefPilotId = string.IsNullOrWhiteSpace(SimBriefPilotId) ? null : SimBriefPilotId.Trim(),
        LastPlnPath = string.IsNullOrWhiteSpace(PlnPath) ? null : PlnPath.Trim(),
        VoiceEnabled = VoiceEnabled,
        Volume = (float)Math.Clamp(Volume / 100, 0, 1),
        RadioFilter = RadioFilter,
        VoiceModels = Voices.Where(v => v.Enabled).Select(v => v.Model.Id).ToList() is { Count: > 0 } chosen ? chosen : _original.VoiceModels,
        PushToTalkInput = string.IsNullOrWhiteSpace(PushToTalkInput) ? null : PushToTalkInput.Trim(),
        UseAtcKey = UseAtcKey,
        KeyboardPushToTalk = string.IsNullOrWhiteSpace(KeyboardPushToTalk) ? null : KeyboardPushToTalk.Trim(),
        VoiceInput = VoiceInput,
        MicrophoneDevice = Microphone.Number,
        WhisperModel = WhisperModel,
        TaxiArrows = TaxiArrows,
        RecordFlights = RecordFlights,
        RecordVoiceClips = RecordVoiceClips,
        ArrowLookAheadM = Math.Clamp(ArrowLookAheadM, 50, 1000),
        ArrowSpacingM = Math.Clamp(ArrowSpacingM, 3, 50),
        Language = Language.Code,
    };

    /// <summary>Speaks a sample call with one of the chosen voices (downloading it first if needed).</summary>
    [RelayCommand]
    private async Task TestVoice()
    {
        var model = Voices.FirstOrDefault(v => v.Enabled)?.Model ?? VoiceCatalog.All[0];
        var store = new VoiceStore();
        try
        {
            if (!store.IsInstalled(model))
            {
                TestStatus = $"Downloading {model.Id}...";
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                await store.InstallAsync(model, http);
            }
            TestStatus = model.Id;
            var voice = new VoiceRef(model.Id, model.Speakers[Random.Shared.Next(model.Speakers.Count)]);
            var clip = await Task.Run(() =>
            {
                using var tts = new PiperSynthesizer(store);
                var clean = tts.Speak("Iberia one two three four, climb flight level one two zero, contact Madrid Control one three three decimal one.", voice);
                return RadioFilter ? OpenSkyAtc.Voice.RadioFilter.Apply(clean, 1) : clean;
            });
            var player = new ClipPlayer { Volume = (float)Math.Clamp(Volume / 100, 0, 1) };
            player.Finished += player.Dispose;
            player.Play(clip);
            TestStatus = voice.ToString();
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or InvalidOperationException or TaskCanceledException)
        {
            TestStatus = e.Message;
        }
    }
}

public sealed record MicrophoneChoice(int Number, string Name)
{
    public override string ToString() => Name;
}
