using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenSkyAtc.App.ViewModels;
using OpenSkyAtc.App.Views;
using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Host;

namespace OpenSkyAtc.App;

public partial class App : Application
{
    private MainViewModel? _main;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CrashLog.Install(this);
        Palette.Apply(this);
        if (e.Args.Length >= 2 && e.Args[0] == "--debrief-html")
        {
            // Developer check of the HTML export: writes the sample debrief and exits.
            Loc.Instance.Language = e.Args.Length > 2 ? e.Args[2] : "en";
            File.WriteAllText(e.Args[1], new DebriefViewModel(SampleDebrief()).ToHtml());
            Shutdown();
            return;
        }
        if (e.Args.Length >= 2 && e.Args[0] == "--screenshot")
        {
            Screenshot(e.Args[1], e.Args.Length > 2 ? e.Args[2] : "main", e.Args.Length > 3 ? e.Args[3] : "en");
            return;
        }
        _main = new MainViewModel(new SettingsStore(), Dispatcher);
        var window = new MainWindow(_main);
        MainWindow = window;
        window.Show();
        if (_main.Settings.CompactMode) window.ShowCompact();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _main?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Developer check: "--screenshot FILE.png [main|compact|settings[:TAB]|readback|debrief|past] [en|es]" renders one window with
    /// sample content to a PNG and exits (settings come from a throwaway file, never the user's; TAB is the settings
    /// tab number, from 0).
    /// </summary>
    private static SettingsWindow SettingsAt(MainViewModel vm, string which) => new(new SettingsViewModel(vm.Settings));

    private static SavedDebrief SampleDebrief()
    {
        var time = (int m, int s) => new TimeSpan(0, m, s);
        return new SavedDebrief(@"C:\sample\20261018-141200 LEMD-LEBL IBE1234.json", new Core.Debrief.FlightDebrief
        {
            Callsign = "IBE1234", Origin = "LEMD", Destination = "LEBL", AircraftType = "A320",
            EndedAt = new DateTimeOffset(2026, 10, 18, 14, 12, 0, TimeSpan.FromHours(2)), Duration = new TimeSpan(1, 12, 30),
            Completed = true, LastPhase = Core.Model.FlightPhase.Parked, Score = 78, Readbacks = 11, ReadbacksCorrect = 9,
            Findings =
            [
                new(time(14, 20), Core.Debrief.FindingKind.Conformance, Core.Debrief.FindingSeverity.Serious, 20, Check: Core.Conformance.ConformanceCheck.RunwayIncursion, Level: 1, Unit: ""),
                new(time(31, 5), Core.Debrief.FindingKind.Conformance, Core.Debrief.FindingSeverity.Deviation, 10, Check: Core.Conformance.ConformanceCheck.AltitudeBust, Level: 3, Worst: 650, Unit: "ft"),
                new(time(52, 40), Core.Debrief.FindingKind.Conformance, Core.Debrief.FindingSeverity.Warning, 2, Check: Core.Conformance.ConformanceCheck.Speed, Level: 1, Worst: 18, Unit: "kt"),
                new(time(2, 10), Core.Debrief.FindingKind.ReadbackWrong, Core.Debrief.FindingSeverity.Readback, 2, Tag: "ifr-clearance", Wrong: [ReadbackItem.Squawk], Missing: []),
                new(time(20, 45), Core.Debrief.FindingKind.ReadbackMissed, Core.Debrief.FindingSeverity.Readback, 3, Tag: "contact-departure"),
                new(time(40, 10), Core.Debrief.FindingKind.HandoffLate, Core.Debrief.FindingSeverity.Handoff, 3, Station: "Madrid Control", Seconds: 71),
            ],
            Handoffs =
            [
                new(time(18, 2), "Madrid Tower", "Madrid Departure", 127_100, 12, true),
                new(time(40, 10), "Madrid Departure", "Madrid Control", 133_100, 71, true),
            ],
        });
    }

    private static System.Windows.Controls.TabControl? FindTabs(DependencyObject root)
    {
        if (root is System.Windows.Controls.TabControl tabs) return tabs;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            if (FindTabs(child) is { } found) return found;
        return null;
    }

    private void Screenshot(string file, string which, string language)
    {
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "openskyatc-screenshot-settings.json"));
        store.Save(new AppSettings { Language = language, SimBriefPilotId = "1234567" });
        var vm = new MainViewModel(store, Dispatcher);
        vm.ShowSample();
        var sample = new Readback(new Dictionary<ReadbackItem, string>
        {
            [ReadbackItem.Destination] = "LEBL", [ReadbackItem.Route] = "FLIGHT PLANNED ROUTE",
            [ReadbackItem.InitialClimb] = "5000FT", [ReadbackItem.Squawk] = "4774",
        });
        Window window = which switch
        {
            "compact" => new CompactWindow(vm, new MainWindow(vm)),
            _ when which.StartsWith("settings", StringComparison.Ordinal) => SettingsAt(vm, which),
            "readback" => new ReadbackWindow(new ReadbackFormViewModel(sample)),
            "debrief" => new DebriefWindow(new DebriefViewModel(SampleDebrief())),
            "past" => new PastFlightsWindow([SampleDebrief(), SampleDebrief() with { Debrief = SampleDebrief().Debrief with { Score = 100, Findings = [], EndedAt = SampleDebrief().Debrief.EndedAt!.Value.AddDays(-1) } }]),
            _ => new MainWindow(vm),
        };
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        if (window is SettingsWindow && which.Split(':') is [_, var tab] && int.TryParse(tab, out int index))
            window.Loaded += (_, _) => { if (FindTabs(window) is { } tabs) tabs.SelectedIndex = index; };
        window.Left = -10_000; // off screen
        window.ContentRendered += (_, _) => Dispatcher.InvokeAsync(() =>
        {
            var content = (FrameworkElement)window.Content;
            var dpi = VisualTreeHelper.GetDpi(window);
            // The drawn extent, so anything spilling past the window shows up in the picture.
            var drawn = VisualTreeHelper.GetDescendantBounds(content);
            // Rendering keeps the content's margin offset, so the picture needs the margin too.
            var m = content.Margin;
            double width = Math.Max(content.ActualWidth, drawn.Right) + m.Left + m.Right, height = Math.Max(content.ActualHeight, drawn.Bottom) + m.Top + m.Bottom;
            Console.WriteLine($"{which}: window content {content.ActualWidth:0}x{content.ActualHeight:0}, drawn {drawn.Right:0}x{drawn.Bottom:0}");
            var bitmap = new RenderTargetBitmap((int)(width * dpi.DpiScaleX), (int)(height * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            var background = new DrawingVisual();
            using (var dc = background.RenderOpen())
                dc.DrawRectangle(window.TryFindResource("ApplicationBackgroundBrush") as Brush ?? window.TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush ?? window.Background ?? Brushes.White, null, new Rect(0, 0, width, height));
            bitmap.Render(background);
            bitmap.Render(content);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(file)) png.Save(stream);
            Shutdown();
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        window.Show();
    }
}
