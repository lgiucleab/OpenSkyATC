using System.IO;
using System.Windows;
using Microsoft.Win32;
using OpenSkyAtc.App.ViewModels;

namespace OpenSkyAtc.App.Views;

/// <summary>The end-of-flight debrief (step 16): score, findings by severity, readback and handoff summary, HTML export.</summary>
public partial class DebriefWindow : Window
{
    private readonly DebriefViewModel _vm;

    public DebriefWindow(DebriefViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "HTML (*.html)|*.html",
            FileName = Path.GetFileNameWithoutExtension(_vm.Path) + ".html",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, _vm.ToHtml());
            ExportStatus.Text = Loc.Format("Debrief.Exported", Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, Loc.Get("Debrief.Export"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
