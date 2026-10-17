using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace OpenSkyAtc.App;

/// <summary>
/// UI strings in the chosen language (English or Spanish, from Resources/Strings*.resx), bindable from XAML as
/// <c>{Binding [Main.Settings], Source={x:Static app:Loc.Instance}}</c>. Changing <see cref="Language"/> updates every
/// bound text at once.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    private static readonly ResourceManager Resources = new("OpenSkyAtc.App.Resources.Strings", typeof(Loc).Assembly);

    public static Loc Instance { get; } = new();

    private CultureInfo _culture = CultureInfo.GetCultureInfo("en");

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => Resources.GetString(key, _culture) ?? key;

    public static string Get(string key) => Instance[key];

    public static string Format(string key, params object?[] args) => string.Format(Instance._culture, Instance[key], args);

    /// <summary>"en" or "es".</summary>
    public string Language
    {
        get => _culture.TwoLetterISOLanguageName;
        set
        {
            _culture = CultureInfo.GetCultureInfo(value is "es" ? "es" : "en");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        }
    }
}
