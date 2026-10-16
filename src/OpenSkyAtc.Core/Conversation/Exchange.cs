using OpenSkyAtc.Core.Messages;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Radio;

namespace OpenSkyAtc.Core.Conversation;

public enum ExchangeState
{
    /// <summary>ATC's transmission is queued and not yet sent.</summary>
    Queued,

    /// <summary>Sent; waiting for the pilot's readback.</summary>
    AwaitingReadback,

    /// <summary>Read back correctly.</summary>
    Correct,

    /// <summary>No readback after one repeat. A late readback is still accepted.</summary>
    Missed,

    /// <summary>Sent, and no readback was required.</summary>
    Done,

    /// <summary>Dropped because the aircraft left the frequency before it was sent.</summary>
    Dropped,
}

/// <summary>
/// One radio interaction: an ATC transmission, the readback it expects, and the verdict.
/// Corrections and repeats belong to the same exchange.
/// </summary>
public sealed class Exchange
{
    private List<KeyValuePair<ReadbackItem, string>> _outstanding;

    internal Exchange(int id, ControllerUnit unit, IReadOnlyList<AtcMessagePart> parts, ReadbackExpectation? expectation, string? tag)
    {
        Id = id;
        Unit = unit;
        Parts = parts;
        Expectation = expectation;
        Tag = tag;
        _outstanding = expectation?.Items.ToList() ?? [];
    }

    public int Id { get; }
    public ControllerUnit Unit { get; }

    /// <summary>The original instruction.</summary>
    public IReadOnlyList<AtcMessagePart> Parts { get; }

    public ReadbackExpectation? Expectation { get; }
    public string? Tag { get; }
    public ExchangeState State { get; internal set; } = ExchangeState.Queued;

    /// <summary>Items still to be read back correctly (narrowed after a correction).</summary>
    public IReadOnlyList<KeyValuePair<ReadbackItem, string>> Outstanding => _outstanding;

    /// <summary>Active-clock time the latest transmission of this exchange was sent.</summary>
    public TimeSpan? SentAt { get; internal set; }

    /// <summary>Parts of the latest transmission, repeated when there is no readback.</summary>
    internal IReadOnlyList<AtcMessagePart> LastParts { get; set; } = [];

    public int Repeats { get; internal set; }
    public int Corrections { get; internal set; }

    public bool NeedsReadback => State is ExchangeState.AwaitingReadback or ExchangeState.Missed;

    internal void Narrow(IEnumerable<ReadbackItem> items)
    {
        var keep = items.ToHashSet();
        _outstanding = _outstanding.Where(kv => keep.Contains(kv.Key)).ToList();
    }
}
