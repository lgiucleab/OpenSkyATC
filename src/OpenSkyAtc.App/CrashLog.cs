using System.Windows;

namespace OpenSkyAtc.App;

/// <summary>
/// Writes any unhandled exception to %LOCALAPPDATA%\OpenSkyATC\logs\crash-*.txt before the app closes, and tells the
/// user where it is, so a crash can be reported. Errors on the UI thread are shown and the app keeps running.
/// </summary>
internal static class CrashLog
{
    public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenSkyATC", "logs");

    public static void Install(Application app)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write(e.ExceptionObject as Exception, fatal: true);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write(e.Exception, fatal: false);
            e.SetObserved();
        };
        app.DispatcherUnhandledException += (_, e) =>
        {
            string? file = Write(e.Exception, fatal: false);
            MessageBox.Show($"Something went wrong: {e.Exception.Message}\n\nDetails were saved to {file}", "OpenSkyATC", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        };
    }

    public static string? Write(Exception? e, bool fatal)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string file = Path.Combine(Folder, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(file, $"OpenSkyATC {(fatal ? "closed after" : "caught")} an unhandled error at {DateTime.Now:O}\n{Environment.OSVersion}, .NET {Environment.Version}\n\n{e}");
            return file;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
