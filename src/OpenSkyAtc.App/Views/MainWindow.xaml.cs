using System.Collections.Specialized;
using System.Windows;
using Microsoft.Win32;
using OpenSkyAtc.App.ViewModels;
using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Host;

namespace OpenSkyAtc.App.Views;

public partial class MainWindow : Window, IMainView
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _vm.View = this;
        DataContext = vm;
        Restore(this, vm.Settings.MainWindow);
        ((INotifyCollectionChanged)vm.Transcript).CollectionChanged += (_, _) => TranscriptScroll.ScrollToEnd();
        PreviewKeyDown += OnKey;
        Closing += (_, _) => _vm.SaveSettings(_vm.Settings with { MainWindow = Placement(this) ?? _vm.Settings.MainWindow });
    }

    /// <summary>Keys 1-9 (top row or keypad) send the call with that number, unless a text box has the focus.</summary>
    private void OnKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase || System.Windows.Input.Keyboard.Modifiers != 0) return;
        int n = e.Key switch
        {
            >= System.Windows.Input.Key.D1 and <= System.Windows.Input.Key.D9 => e.Key - System.Windows.Input.Key.D0,
            >= System.Windows.Input.Key.NumPad1 and <= System.Windows.Input.Key.NumPad9 => e.Key - System.Windows.Input.Key.NumPad0,
            _ => 0,
        };
        if (n > 0 && _vm.SendNumber(n)) e.Handled = true;
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    /// <summary>Opens the flight logs folder in Explorer (to send one when reporting a problem).</summary>
    private void FlightLogs_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(FlightLogWriter.Folder);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{FlightLogWriter.Folder}\"") { UseShellExecute = true });
    }

    private void PastFlights_Click(object sender, RoutedEventArgs e) =>
        new PastFlightsWindow(new DebriefStore().List()) { Owner = this }.Show();

    public void ShowDebrief(SavedDebrief debrief)
    {
        var window = new DebriefWindow(new DebriefViewModel(debrief)) { Owner = this };
        window.Show();
        window.Activate();
    }

    public Readback? AskReadback(Readback expected)
    {
        var dialog = new ReadbackWindow(new ReadbackFormViewModel(expected)) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    public string? PickPlnFile()
    {
        var dialog = new OpenFileDialog { Filter = Loc.Get("Main.PlnFilter"), CheckFileExists = true };
        if (_vm.Settings.LastPlnPath is { } last && Path.GetDirectoryName(last) is { } dir && Directory.Exists(dir)) dialog.InitialDirectory = dir;
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    public void OpenSettings()
    {
        var dialog = new SettingsWindow(new SettingsViewModel(_vm.Settings)) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } settings) _vm.SaveSettings(settings);
    }

    private void GettingStarted_Click(object sender, RoutedEventArgs e) => OpenWelcome();

    /// <summary>The first-run guide; its answers (or, when skipped, only "seen") are saved.</summary>
    public void OpenWelcome()
    {
        var dialog = new WelcomeWindow(new WelcomeViewModel(_vm.Settings)) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Result is { } settings) _vm.SaveSettings(settings);
    }

    public void ShowError(string title, string message) =>
        MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    /// <summary>Where the window is; null when it has never been shown (its bounds are then empty, i.e. infinite).</summary>
    internal static WindowPlacement? Placement(Window w)
    {
        var r = w.RestoreBounds;
        bool finite = !r.IsEmpty && double.IsFinite(r.Left) && double.IsFinite(r.Top) && double.IsFinite(r.Width) && double.IsFinite(r.Height);
        return finite ? new WindowPlacement(r.Left, r.Top, r.Width, r.Height) : null;
    }

    /// <summary>Puts a window where it was, if that place is still on a screen.</summary>
    internal static void Restore(Window w, WindowPlacement? p)
    {
        if (p is null || p.Width < 100 || p.Height < 60) return;
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!screen.IntersectsWith(new Rect(p.Left, p.Top, p.Width, p.Height))) return;
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        (w.Left, w.Top, w.Width, w.Height) = (p.Left, p.Top, p.Width, p.Height);
    }
}
