using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenSkyAtc.Host;

namespace OpenSkyAtc.App.ViewModels;

/// <summary>
/// The first-run guide (owner's choice, after 1.0.0-alpha): three steps that fill the settings a first flight needs
/// (flight plan source, push-to-talk and voice) and say how to start it. Shown once, when neither a SimBrief ID nor a
/// .pln is set; Gear > Getting started shows it again.
/// </summary>
public sealed partial class WelcomeViewModel : ObservableObject
{
    public const int Steps = 3;

    private readonly AppSettings _original;

    public WelcomeViewModel(AppSettings s)
    {
        _original = s;
        _simBriefPilotId = s.SimBriefPilotId ?? "";
        _plnPath = s.LastPlnPath ?? "";
        _useSimBrief = s.PlanSource == PlanSource.SimBrief || string.IsNullOrEmpty(s.LastPlnPath);
        _keyboardPushToTalk = s.KeyboardPushToTalk ?? "";
        _useAtcKey = s.UseAtcKey;
        _voiceInput = s.VoiceInput;
    }

    /// <summary>Shown when the app starts: the guide has never run and no plan source is set.</summary>
    public static bool ShouldShow(AppSettings s) =>
        !s.FirstRunDone && string.IsNullOrWhiteSpace(s.SimBriefPilotId) && string.IsNullOrWhiteSpace(s.LastPlnPath);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlanStep), nameof(IsPttStep), nameof(IsFlyStep), nameof(CanGoBack), nameof(IsLast), nameof(StepText))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    private int _step;

    [ObservableProperty] private bool _useSimBrief;
    [ObservableProperty] private string _simBriefPilotId;
    [ObservableProperty] private string _plnPath;
    [ObservableProperty] private string _keyboardPushToTalk;
    [ObservableProperty] private bool _useAtcKey;
    [ObservableProperty] private bool _voiceInput;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool HasError => Error is not null;

    public bool IsPlanStep => Step == 0;
    public bool IsPttStep => Step == 1;
    public bool IsFlyStep => Step == 2;
    public bool CanGoBack => Step > 0;
    public bool IsLast => Step == Steps - 1;
    public string StepText => Loc.Format("Welcome.StepOf", Step + 1, Steps);

    public bool UsePln
    {
        get => !UseSimBrief;
        set => UseSimBrief = !value;
    }

    partial void OnUseSimBriefChanged(bool value) => OnPropertyChanged(nameof(UsePln));

    /// <summary>Next step; false when the current step is not filled in correctly (the error says why).</summary>
    public bool Next()
    {
        Error = Validate();
        if (Error is not null) return false;
        if (Step < Steps - 1) Step++;
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        Error = null;
        if (Step > 0) Step--;
    }

    private string? Validate()
    {
        if (IsPlanStep)
        {
            if (UseSimBrief && !(SimBriefPilotId.Trim() is { Length: > 0 and <= 7 } id && id.All(char.IsAsciiDigit)))
                return Loc.Get("Welcome.NeedSimBriefId");
            if (UsePln && string.IsNullOrWhiteSpace(PlnPath)) return Loc.Get("Welcome.NeedPln");
        }
        if (IsPttStep && !string.IsNullOrWhiteSpace(KeyboardPushToTalk) && Host.KeyboardPushToTalk.Parse(KeyboardPushToTalk) is null)
            return Loc.Format("Welcome.UnknownKey", KeyboardPushToTalk.Trim());
        return null;
    }

    /// <summary>The settings with the guide's answers; <paramref name="completed"/> false when it was skipped.</summary>
    public AppSettings ToSettings(bool completed) => completed
        ? _original with
        {
            FirstRunDone = true,
            PlanSource = UseSimBrief ? PlanSource.SimBrief : PlanSource.PlnFile,
            SimBriefPilotId = string.IsNullOrWhiteSpace(SimBriefPilotId) ? _original.SimBriefPilotId : SimBriefPilotId.Trim(),
            LastPlnPath = string.IsNullOrWhiteSpace(PlnPath) ? _original.LastPlnPath : PlnPath.Trim(),
            KeyboardPushToTalk = string.IsNullOrWhiteSpace(KeyboardPushToTalk) ? null : KeyboardPushToTalk.Trim(),
            UseAtcKey = UseAtcKey,
            VoiceInput = VoiceInput,
        }
        : _original with { FirstRunDone = true };
}
