using OpenSkyAtc.Core.Messages;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Radio;
using OpenSkyAtc.Core.Random;

namespace OpenSkyAtc.Core.Conversation;

/// <summary>Response delays and readback timing (design doc "Response timing").</summary>
public sealed record ConversationTiming
{
    public (double Min, double Max) ReplySeconds { get; init; } = (1.5, 4);
    public (double Min, double Max) CorrectionSeconds { get; init; } = (1, 2);
    public (double Min, double Max) CheckInSeconds { get; init; } = (2, 5);

    /// <summary>No readback within this time: repeat once, then mark the exchange missed.</summary>
    public TimeSpan ReadbackTimeout { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Owns the exchanges: queues ATC transmissions, checks readbacks, sends corrections, repeats ignored
/// instructions once and marks them missed. Phases call <see cref="Transmit"/>; the brain routes readbacks here.
/// </summary>
public sealed class ConversationManager(TransmissionQueue queue, IRandom rng, EventLog log, ConversationTiming? timing = null)
{
    private readonly ConversationTiming _timing = timing ?? new ConversationTiming();
    private readonly List<Exchange> _exchanges = [];
    private readonly Dictionary<ControllerUnit, QueuedTransmission> _lastSent = [];
    private int _nextId = 1;

    public IReadOnlyList<Exchange> Exchanges => _exchanges;

    public TimeSpan ReplyDelay() => Sample(_timing.ReplySeconds);
    public TimeSpan CorrectionDelay() => Sample(_timing.CorrectionSeconds);
    public TimeSpan CheckInDelay() => Sample(_timing.CheckInSeconds);

    /// <summary>Queues an ATC transmission as a new exchange.</summary>
    /// <param name="due">Active-clock time it may be sent.</param>
    /// <param name="readback">What the pilot must read back; null when no readback is required.</param>
    /// <param name="onSent">Updates the clearance record when the transmission is actually sent.</param>
    /// <param name="onReadBack">Runs when the pilot reads it back correctly.</param>
    public Exchange Transmit(ControllerUnit unit, IReadOnlyList<AtcMessagePart> parts, TransmissionPriority priority, TimeSpan due,
        ReadbackExpectation? readback = null, Action? onSent = null, string? tag = null, Action? onReadBack = null)
    {
        var exchange = new Exchange(_nextId++, unit, parts, readback, tag) { OnReadBack = onReadBack };
        _exchanges.Add(exchange);
        exchange.LastParts = parts;
        queue.Enqueue(unit, parts, priority, due, exchange, onSent);
        return exchange;
    }

    /// <summary>Queues a transmission that is not an exchange (stand by, say again, unable).</summary>
    public void Say(ControllerUnit unit, IReadOnlyList<AtcMessagePart> parts, TransmissionPriority priority, TimeSpan due) =>
        queue.Enqueue(unit, parts, priority, due);

    /// <summary>True while the unit has an exchange waiting to be sent or read back (one open exchange per frequency).</summary>
    public bool IsBusy(ControllerUnit unit) =>
        _exchanges.Any(e => e.Unit == unit && e.State is ExchangeState.Queued or ExchangeState.AwaitingReadback);

    /// <summary>The exchange a readback button should target: the latest one still needing a readback.</summary>
    public Exchange? PendingReadback() => _exchanges.LastOrDefault(e => e.NeedsReadback);

    /// <summary>Checks a readback. Returns false when there is nothing on this unit to read back.</summary>
    internal bool HandleReadback(ControllerUnit unit, IReadOnlyDictionary<ReadbackItem, string> items, int? exchangeId, TimeSpan now, TimeSpan simTime)
    {
        var exchange = exchangeId is { } id
            ? _exchanges.FirstOrDefault(e => e.Id == id && e.Unit == unit && e.NeedsReadback)
            : _exchanges.LastOrDefault(e => e.Unit == unit && e.NeedsReadback);
        if (exchange is null) return false;

        var verdict = ReadbackVerifier.Check(exchange.Outstanding, items);
        log.Add(new ReadbackChecked(simTime, exchange.Id, exchange.Tag, verdict));
        if (verdict.IsCorrect)
        {
            exchange.State = ExchangeState.Correct;
            exchange.OnReadBack?.Invoke();
            return true;
        }

        // Correct wrong items first ("Negative, squawk 4521"); otherwise ask for the missing ones.
        IReadOnlyList<AtcMessagePart> parts = verdict.Wrong.Count > 0
            ? [new Negative(Repeated: exchange.Corrections > 0), .. verdict.Wrong.SelectMany(i => exchange.Expectation!.Corrections[i])]
            : [new ReadBackRequest(verdict.Missing)];
        exchange.Narrow(verdict.Wrong.Concat(verdict.Missing));
        exchange.Corrections++;
        exchange.Repeats = 0;
        exchange.State = ExchangeState.Queued;
        exchange.LastParts = parts;
        queue.Enqueue(unit, parts, TransmissionPriority.Reply, now + CorrectionDelay(), exchange);
        return true;
    }

    /// <summary>Repeats the unit's last transmission.</summary>
    internal bool SayAgain(ControllerUnit unit, TimeSpan now)
    {
        if (!_lastSent.TryGetValue(unit, out var last)) return false;
        if (last.Exchange is { NeedsReadback: true } ex) ex.State = ExchangeState.Queued;
        queue.Enqueue(unit, last.Parts, TransmissionPriority.Reply, now + CorrectionDelay(), last.Exchange);
        return true;
    }

    /// <summary>Called by the brain when a queued transmission goes out.</summary>
    internal void OnSent(QueuedTransmission t, TimeSpan now)
    {
        _lastSent[t.Unit] = t;
        if (t.Exchange is not { } ex || ex.State is ExchangeState.Correct) return;
        ex.SentAt = now;
        ex.State = ex.Outstanding.Count > 0 ? ExchangeState.AwaitingReadback : ExchangeState.Done;
    }

    /// <summary>The transmission finished playing: the readback timeout counts from now (only while still awaited).</summary>
    internal void Heard(Exchange ex, TimeSpan now)
    {
        if (ex.State is ExchangeState.AwaitingReadback && ex.SentAt is { } sent && now > sent) ex.SentAt = now;
    }

    internal void OnDropped(QueuedTransmission t)
    {
        if (t.Exchange is { State: ExchangeState.Queued } ex) ex.State = ExchangeState.Dropped;
    }

    /// <summary>Repeats an unanswered instruction once, then marks it missed.</summary>
    internal void Tick(TimeSpan now, TimeSpan simTime)
    {
        foreach (var ex in _exchanges)
        {
            if (ex.State != ExchangeState.AwaitingReadback || ex.SentAt is not { } sent || now - sent < _timing.ReadbackTimeout) continue;
            if (ex.Repeats == 0)
            {
                ex.Repeats = 1;
                ex.State = ExchangeState.Queued;
                queue.Enqueue(ex.Unit, ex.LastParts, TransmissionPriority.Instruction, now, ex);
            }
            else
            {
                ex.State = ExchangeState.Missed;
                log.Add(new ReadbackMissed(simTime, ex.Id, ex.Tag));
            }
        }
    }

    private TimeSpan Sample((double Min, double Max) range) =>
        TimeSpan.FromSeconds(Math.Round(range.Min + rng.NextDouble() * (range.Max - range.Min), 2));
}
