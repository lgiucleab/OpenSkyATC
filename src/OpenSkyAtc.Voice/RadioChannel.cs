using OpenSkyAtc.Core.Messages;
using OpenSkyAtc.Core.Radio;

namespace OpenSkyAtc.Voice;

/// <summary>A transmission ready to play: who says it, on what frequency, how urgent, and the audio.</summary>
/// <param name="Message">The brain's message, told back to it when heard (<see cref="Core.Brain.IAtcBrain.TransmissionHeard"/>).</param>
public sealed record RadioTransmission(string Unit, int FrequencyKhz, TransmissionPriority Priority, AudioClip Clip, string Text, AtcMessage? Message = null)
{
    public TimeSpan Duration => Clip.Duration;
}

/// <summary>What the player must do now.</summary>
public abstract record RadioAction(RadioTransmission Transmission);
public sealed record StartPlaying(RadioTransmission Transmission) : RadioAction(Transmission);
public sealed record CutOff(RadioTransmission Transmission) : RadioAction(Transmission);
/// <summary>Finished playing (to the end, or stopped from outside): the pilot has heard it.</summary>
public sealed record FinishedPlaying(RadioTransmission Transmission) : RadioAction(Transmission);
/// <summary>Never played: the aircraft was no longer on that frequency when its turn came (design doc rule 3).</summary>
public sealed record Dropped(RadioTransmission Transmission) : RadioAction(Transmission);

/// <summary>
/// The playback side of the design doc's transmission queue rules (Core decides what to say and when it is due;
/// this decides when it can be heard). Pure: time is passed in, so it is unit-tested without audio.
/// <list type="number">
/// <item>Never start while the pilot is transmitting (push-to-talk held) or while another transmission plays.</item>
/// <item>Leave a gap after any transmission ends (1 s).</item>
/// <item>If the aircraft is not on the transmission's frequency when its turn comes, drop it.</item>
/// <item>Urgent transmissions cut the current one and play at once, without the gap (but still not over the pilot:
/// on a real radio the pilot cannot hear ATC while transmitting).</item>
/// </list>
/// Waiting transmissions are taken by priority, then in arrival order.
/// </summary>
public sealed class RadioChannel
{
    private readonly List<(RadioTransmission T, long Order)> _waiting = [];
    private long _order;
    private RadioTransmission? _playing;
    private TimeSpan _playingEnds;
    private TimeSpan _quietUntil = TimeSpan.MinValue;
    private bool _pilotTransmitting;
    private RadioTransmission? _stoppedEarly;

    public TimeSpan Gap { get; init; } = TimeSpan.FromSeconds(1);

    public RadioTransmission? Playing => _playing;
    public int Waiting => _waiting.Count;

    public void Enqueue(RadioTransmission t) => _waiting.Add((t, _order++));

    public void PushToTalk(bool held, TimeSpan now)
    {
        // Releasing the button counts as the end of a transmission: ATC answers after the gap, not on top of it.
        if (_pilotTransmitting && !held) _quietUntil = Max(_quietUntil, now + Gap);
        _pilotTransmitting = held;
    }

    /// <summary>The player finished early (or was stopped from outside); the gap starts now.</summary>
    public void Stopped(TimeSpan now)
    {
        if (_playing is null) return;
        _stoppedEarly = _playing;
        _playing = null;
        _quietUntil = now + Gap;
    }

    /// <param name="isTuned">Whether COM1 or COM2 is on a frequency now.</param>
    public IReadOnlyList<RadioAction> Update(TimeSpan now, Func<int, bool> isTuned)
    {
        var actions = new List<RadioAction>();
        if (_stoppedEarly is not null)
        {
            actions.Add(new FinishedPlaying(_stoppedEarly));
            _stoppedEarly = null;
        }
        if (_playing is not null && now >= _playingEnds)
        {
            actions.Add(new FinishedPlaying(_playing));
            _playing = null;
            _quietUntil = _playingEnds + Gap;
        }

        while (Next() is { } next)
        {
            bool urgent = next.T.Priority == TransmissionPriority.Urgent;
            if (_pilotTransmitting) break;
            if (_playing is not null && !(urgent && _playing.Priority != TransmissionPriority.Urgent)) break;
            if (!urgent && now < _quietUntil) break;

            _waiting.Remove(next);
            if (!isTuned(next.T.FrequencyKhz))
            {
                actions.Add(new Dropped(next.T));
                continue;
            }
            if (_playing is not null)
            {
                actions.Add(new CutOff(_playing));
                actions.Add(new FinishedPlaying(_playing));
            }
            _playing = next.T;
            _playingEnds = now + next.T.Duration;
            actions.Add(new StartPlaying(next.T));
            break;
        }
        return actions;
    }

    private (RadioTransmission T, long Order)? Next() =>
        _waiting.Count == 0 ? null : _waiting.OrderBy(w => w.T.Priority).ThenBy(w => w.Order).First();

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
