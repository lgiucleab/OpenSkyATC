namespace OpenSkyAtc.Core.Events;

/// <summary>
/// Fires once when the fire condition has held for <c>hold</c>, then stays quiet until the re-arm
/// condition has held for <c>rearm</c>. Starts disarmed unless told otherwise, so a condition that is
/// already true when observation starts does not fire.
/// </summary>
internal sealed class HeldTrigger(TimeSpan hold, TimeSpan rearm, bool startArmed = false)
{
    private bool _armed = startArmed;
    private TimeSpan? _fireSince;
    private TimeSpan? _rearmSince;

    /// <summary>Start of the held period that led to the latest fire.</summary>
    public TimeSpan LastFireStart { get; private set; }

    public bool Update(bool fire, bool rearmCondition, TimeSpan now)
    {
        _rearmSince = rearmCondition ? _rearmSince ?? now : null;
        if (!_armed && _rearmSince is { } r && now - r >= rearm) _armed = true;

        _fireSince = fire ? _fireSince ?? now : null;
        if (_armed && _fireSince is { } f && now - f >= hold)
        {
            _armed = false;
            LastFireStart = f;
            return true;
        }
        return false;
    }

    public void Reset(bool armed = false)
    {
        _armed = armed;
        _fireSince = null;
        _rearmSince = null;
    }
}

/// <summary>
/// Reports a new value once it has been stable for <c>hold</c>. The first value observed is the
/// baseline and is not reported. Flickers shorter than the hold time are ignored.
/// </summary>
internal sealed class StableValue<T>(TimeSpan hold)
{
    private static readonly EqualityComparer<T> Eq = EqualityComparer<T>.Default;

    private bool _hasValue;
    private T _value = default!;
    private T _candidate = default!;
    private TimeSpan _since;

    public T Value => _value;

    public bool Update(T value, TimeSpan now)
    {
        if (!_hasValue)
        {
            _hasValue = true;
            _value = _candidate = value;
            _since = now;
            return false;
        }
        if (!Eq.Equals(value, _candidate))
        {
            _candidate = value;
            _since = now;
        }
        if (!Eq.Equals(_candidate, _value) && now - _since >= hold)
        {
            _value = _candidate;
            return true;
        }
        return false;
    }

    public void Reset() => _hasValue = false;
}

/// <summary>
/// Detects a value crossing a threshold, with a hysteresis band. A crossing upward fires when the value
/// reaches the threshold, but only after it has been below threshold - band; downward likewise.
/// Noise around the threshold therefore fires at most once per real crossing.
/// </summary>
internal sealed class ThresholdCrossing(double threshold, double band)
{
    private bool _armedUp;
    private bool _armedDown;

    /// <summary>+1 for an upward crossing, -1 for downward, 0 for none.</summary>
    public int Update(double value)
    {
        if (value < threshold - band) _armedUp = true;
        if (value > threshold + band) _armedDown = true;

        if (_armedUp && value >= threshold)
        {
            _armedUp = false;
            return +1;
        }
        if (_armedDown && value <= threshold)
        {
            _armedDown = false;
            return -1;
        }
        return 0;
    }

    public void Reset() => _armedUp = _armedDown = false;
}
