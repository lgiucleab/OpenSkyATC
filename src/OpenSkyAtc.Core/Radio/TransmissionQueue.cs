using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Messages;
using OpenSkyAtc.Core.Model;

namespace OpenSkyAtc.Core.Radio;

/// <summary>Queue order, highest first (design doc: urgent, reply, instruction, info).</summary>
public enum TransmissionPriority
{
    Urgent,
    Reply,
    Instruction,
    Info,
}

public sealed class QueuedTransmission
{
    internal QueuedTransmission(long sequence, ControllerUnit unit, IReadOnlyList<AtcMessagePart> parts, TransmissionPriority priority, TimeSpan due, Exchange? exchange, Action? onSent)
    {
        Sequence = sequence;
        Unit = unit;
        Parts = parts;
        Priority = priority;
        Due = due;
        Exchange = exchange;
        OnSent = onSent;
    }

    public long Sequence { get; }
    public ControllerUnit Unit { get; }
    public IReadOnlyList<AtcMessagePart> Parts { get; }
    public TransmissionPriority Priority { get; }

    /// <summary>Active-clock time at which the transmission may start.</summary>
    public TimeSpan Due { get; }

    public Exchange? Exchange { get; }

    /// <summary>Runs when the transmission is actually sent; updates the clearance record.</summary>
    internal Action? OnSent { get; }
}

/// <summary>
/// The logical transmission queue: what is due, on which frequency, in which order. Audio playback,
/// preemption and not talking over push-to-talk are handled by the voice layer.
/// </summary>
public sealed class TransmissionQueue
{
    private readonly List<QueuedTransmission> _items = [];
    private long _sequence;

    public IReadOnlyList<QueuedTransmission> Pending => _items;

    public QueuedTransmission Enqueue(ControllerUnit unit, IReadOnlyList<AtcMessagePart> parts, TransmissionPriority priority, TimeSpan due,
        Exchange? exchange = null, Action? onSent = null)
    {
        var item = new QueuedTransmission(_sequence++, unit, parts, priority, due, exchange, onSent);
        _items.Add(item);
        return item;
    }

    /// <summary>Removes and returns every transmission due at <paramref name="now"/>, by priority, then due time, then order queued.</summary>
    public IReadOnlyList<QueuedTransmission> TakeDue(TimeSpan now)
    {
        var due = _items.Where(i => i.Due <= now)
            .OrderBy(i => i.Priority).ThenBy(i => i.Due).ThenBy(i => i.Sequence)
            .ToList();
        foreach (var item in due) _items.Remove(item);
        return due;
    }
}
