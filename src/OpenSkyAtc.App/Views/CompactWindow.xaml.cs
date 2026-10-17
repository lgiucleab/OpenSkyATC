using System.Windows;
using OpenSkyAtc.App.ViewModels;

namespace OpenSkyAtc.App.Views;

/// <summary>The small always-on-top strip (owner's choice, step 13): frequency, last ATC call and the call buttons.</summary>
public partial class CompactWindow : Window
{
    private readonly MainWindow _main;

    public CompactWindow(MainViewModel vm, MainWindow main)
    {
        InitializeComponent();
        DataContext = vm;
        _main = main;
        // Closing the strip goes back to the full window instead of leaving the app without a window.
        Closing += (_, e) =>
        {
            if (!_main.IsClosing)
            {
                e.Cancel = true;
                _main.ShowFull();
            }
        };
    }

    private void Expand_Click(object sender, RoutedEventArgs e) => _main.ShowFull();
}
