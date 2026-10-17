using CommunityToolkit.Mvvm.ComponentModel;
using OpenSkyAtc.Core.Conversation;

namespace OpenSkyAtc.App.ViewModels;

/// <summary>One field of the hard-mode readback form: what to read back, never the expected value.</summary>
public sealed partial class ReadbackField(ReadbackItem item) : ObservableObject
{
    public ReadbackItem Item { get; } = item;
    public string Label => Loc.Get($"Item.{Item}");
    public bool IsChoice => Item == ReadbackItem.Clearance;
    public IReadOnlyList<string> Choices => ReadbackInput.Clearances;

    [ObservableProperty] private string _text = "";
}

/// <summary>Hard mode (step 13): the pilot types each item; values are normalised and checked like any readback.</summary>
public sealed class ReadbackFormViewModel(Readback expected)
{
    public IReadOnlyList<ReadbackField> Fields { get; } = expected.Items.Keys.Select(i => new ReadbackField(i)).ToList();

    public Readback Build() => new(
        Fields.ToDictionary(f => f.Item, f => ReadbackInput.Normalize(f.Item, f.Text)),
        expected.ExchangeId);
}
