using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenSkyAtc.App.ViewModels;
using OpenSkyAtc.Core.Conversation;

namespace OpenSkyAtc.App.Views;

/// <summary>Hard mode: the pilot types each readback item (step 13).</summary>
public partial class ReadbackWindow : Window
{
    private readonly ReadbackFormViewModel _vm;

    public ReadbackWindow(ReadbackFormViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        // Start typing at once in the first field.
        Loaded += (_, _) => MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    public Readback? Result { get; private set; }

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        Result = _vm.Build();
        DialogResult = true;
    }
}
