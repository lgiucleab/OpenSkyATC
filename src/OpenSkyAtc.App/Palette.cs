using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace OpenSkyAtc.App;

/// <summary>
/// The app's own status colours, stronger than the Fluent theme's (owner's request, step 13), in a dark and a light
/// set that follows the Windows app theme and switches when it changes. XAML uses them as DynamicResource
/// "Brush.Success", "Brush.SuccessBg", "Brush.Info", "Brush.InfoBg", "Brush.Caution", "Brush.CautionBg",
/// "Brush.Critical", "Brush.Primary", "Brush.Atc", "Brush.Pilot".
/// </summary>
public static class Palette
{
    private static readonly Dictionary<string, (string Dark, string Light)> Colors = new()
    {
        ["Success"] = ("#4ADE80", "#15803D"),
        ["SuccessBg"] = ("#1E4D30", "#D1FADF"),
        ["Info"] = ("#60A5FA", "#1D4ED8"),
        ["InfoBg"] = ("#1C3A63", "#DBEAFE"),
        ["Caution"] = ("#FBBF24", "#A16207"),
        ["CautionBg"] = ("#4D3708", "#FEF3C7"),
        ["Critical"] = ("#F87171", "#DC2626"),
        ["Primary"] = ("#3B82F6", "#2563EB"),
        ["Atc"] = ("#60A5FA", "#1D4ED8"),
        ["Pilot"] = ("#4ADE80", "#15803D"),
    };

    private static ResourceDictionary? _current;

    public static void Apply(Application app)
    {
        Load(app);
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General) app.Dispatcher.InvokeAsync(() => Load(app));
        };
    }

    private static void Load(Application app)
    {
        bool dark = IsDark();
        var dictionary = new ResourceDictionary();
        foreach (var (name, (darkHex, lightHex)) in Colors)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? darkHex : lightHex));
            brush.Freeze();
            dictionary[$"Brush.{name}"] = brush;
        }
        if (_current is not null) app.Resources.MergedDictionaries.Remove(_current);
        app.Resources.MergedDictionaries.Add(dictionary);
        _current = dictionary;
    }

    /// <summary>Windows' "app mode" setting (dark unless apps are set to light).</summary>
    private static bool IsDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int light || light == 0;
    }
}
