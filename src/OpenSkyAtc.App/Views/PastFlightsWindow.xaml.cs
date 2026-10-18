using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenSkyAtc.App.ViewModels;
using OpenSkyAtc.Host;

namespace OpenSkyAtc.App.Views;

/// <summary>Gear > Past flights: the saved debriefs, newest first; opening one shows its debrief window.</summary>
public partial class PastFlightsWindow : Window
{
    public sealed record Row(SavedDebrief Saved, string Date, string Route, string Callsign, int Score);

    public PastFlightsWindow(IReadOnlyList<SavedDebrief> debriefs)
    {
        InitializeComponent();
        List.ItemsSource = debriefs.Select(s => new Row(s,
            s.Debrief.EndedAt?.LocalDateTime.ToString("g", CultureInfo.CurrentCulture) ?? "",
            $"{s.Debrief.Origin} → {s.Debrief.Destination}", s.Debrief.Callsign, s.Debrief.Score)).ToList();
        Empty.Visibility = debriefs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => OpenButton.IsEnabled = List.SelectedItem is Row;

    private void List_DoubleClick(object sender, MouseButtonEventArgs e) => OpenSelected();

    private void Open_Click(object sender, RoutedEventArgs e) => OpenSelected();

    private void OpenSelected()
    {
        if (List.SelectedItem is Row row) new DebriefWindow(new DebriefViewModel(row.Saved)) { Owner = this }.Show();
    }
}
