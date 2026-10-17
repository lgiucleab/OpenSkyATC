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
    /// Developer check: "--screenshot FILE.png [main|compact|settings|readback] [en|es]" renders one window with
    /// sample content to a PNG and exits (settings come from a throwaway file, never the user's).
    /// </summary>
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
            "settings" => new SettingsWindow(new SettingsViewModel(vm.Settings)),
            "readback" => new ReadbackWindow(new ReadbackFormViewModel(sample)),
            _ => new MainWindow(vm),
        };
        window.WindowStartupLocation = WindowStartupLocation.Manual;
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
