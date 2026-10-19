using System.Windows;
using Microsoft.Win32;
using OpenSkyAtc.App.ViewModels;
using OpenSkyAtc.Host;

namespace OpenSkyAtc.App.Views;

/// <summary>The first-run guide; <see cref="Result"/> holds the settings to save (also when skipped).</summary>
public partial class WelcomeWindow : Window
{
    private readonly WelcomeViewModel _vm;

    public WelcomeWindow(WelcomeViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Closing += (_, _) => Result ??= _vm.ToSettings(completed: false); // closed with the X: as skipped
    }

    public AppSettings? Result { get; private set; }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        bool last = _vm.IsLast;
        if (!_vm.Next() || !last) return;
        Result = _vm.ToSettings(completed: true);
        DialogResult = true;
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        Result = _vm.ToSettings(completed: false);
        DialogResult = false;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = Loc.Get("Main.PlnFilter"), CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
        {
            _vm.PlnPath = dialog.FileName;
            _vm.UsePln = true;
        }
    }
}
