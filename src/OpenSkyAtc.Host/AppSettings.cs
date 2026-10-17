using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSkyAtc.Host;

public enum PlanSource
{
    SimBrief,
    PlnFile,
}

/// <summary>
/// The user's settings (step 13), saved as JSON in %APPDATA%\OpenSkyATC\settings.json. Every field has a default, so
/// an older or partial file still loads.
/// </summary>
public sealed record AppSettings
{
    // Flight plan
    public PlanSource PlanSource { get; init; } = PlanSource.SimBrief;
    public string? SimBriefPilotId { get; init; }
    public string? LastPlnPath { get; init; }

    // Voice
    public bool VoiceEnabled { get; init; } = true;
    public float Volume { get; init; } = 0.8f;
    /// <summary>Voice models used for controllers (ids from the voice catalogue); all of them by default.</summary>
    public IReadOnlyList<string> VoiceModels { get; init; } = ["vctk", "libritts_r", "joe", "norman", "john", "cori"];
    public bool RadioFilter { get; init; } = true;

    // Push-to-talk (step 9; keyboard global hotkey and joystick outside the sim come in step 14)
    public string? PushToTalkInput { get; init; }
    public bool UseAtcKey { get; init; } = true;

    // Speech recognition (step 14)
    public string WhisperModel { get; init; } = "base.en";

    // Taxi arrows (step 10)
    public bool TaxiArrows { get; init; } = true;
    public double ArrowLookAheadM { get; init; } = 300;
    public double ArrowSpacingM { get; init; } = 10;

    // App
    /// <summary>"en" or "es" (app UI only; the radio is always English).</summary>
    public string Language { get; init; } = "en";
    public bool HardMode { get; init; }
    public bool CompactMode { get; init; }

    /// <summary>Main window position and size, restored on start; null for the default.</summary>
    public WindowPlacement? MainWindow { get; init; }
    public WindowPlacement? CompactWindow { get; init; }
}

public sealed record WindowPlacement(double Left, double Top, double Width, double Height);

public sealed class SettingsStore(string? path = null)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Path { get; } = path ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenSkyATC", "settings.json");

    /// <summary>The saved settings; defaults when there is no file or it cannot be read (a broken file is kept as .bad).</summary>
    public AppSettings Load()
    {
        if (!File.Exists(Path)) return new AppSettings();
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), Options) ?? new AppSettings();
        }
        catch (JsonException)
        {
            File.Copy(Path, Path + ".bad", overwrite: true);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        string temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, Path, overwrite: true);
    }
}
