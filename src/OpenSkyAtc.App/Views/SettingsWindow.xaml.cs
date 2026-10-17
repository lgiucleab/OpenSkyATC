using System.Windows;
using Microsoft.Win32;
using OpenSkyAtc.App.ViewModels;
using OpenSkyAtc.Host;

namespace OpenSkyAtc.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    public AppSettings? Result { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Result = _vm.ToSettings();
        DialogResult = true;
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
