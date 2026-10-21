using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Host;

/// <summary>
/// Auto-tune (owner's choice, refactor phase 2): when the pilot reads back a frequency change correctly, the new
/// frequency becomes the active one on the radio the pilot transmits on, and the old one goes to standby.
/// </summary>
public static class AutoTune
{
    /// <summary>The radio to tune (1 or 2) and the frequency, or null when it is already tuned there.</summary>
    public static (int Radio, int Khz)? Plan(SimState s, ControllerUnit unit)
    {
        int radio = s.Com2Transmit && !s.Com1Transmit ? 2 : 1;
        int active = radio == 2 ? s.Com2ActiveKhz : s.Com1ActiveKhz;
        return unit.Answers(active) ? null : (radio, unit.FrequencyKhz);
    }
}

public enum TuneStep
{
    SetStandby,
    Swap,
    SetActive,
}

/// <summary>A radio event to send: <see cref="Khz"/> is unused for a swap.</summary>
public sealed record TuneCommand(TuneStep Step, int Radio, int Khz);

/// <summary>
/// Carries out one auto-tune and checks every step against what the sim reports (after the FlyByWire A380 flight,
/// 2026-10-21: its own radio panel ignored "set standby", and the blind swap that followed scrambled the radios). The
/// new frequency goes into standby, and only once standby shows it is it swapped to active. If standby does not take
/// it, the active frequency is set directly. If that does not take either, the aircraft manages its own radios:
/// auto-tune stops for the rest of the flight and the pilot is told to tune by hand. Pure; the host sends the commands.
/// </summary>
public sealed class AutoTuner
{
    public static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(3);

    private TuneStep? _waitingFor;
    private int _radio, _khz;
    private TimeSpan _since;

    /// <summary>The aircraft did not take a frequency: no more auto-tuning this flight.</summary>
    public bool Disabled { get; private set; }

    public bool Busy => _waitingFor is not null;

    /// <summary>Starts tuning; returns the first command, or null when disabled.</summary>
    public TuneCommand? Start(int radio, int khz, TimeSpan now)
    {
        if (Disabled) return null;
        (_radio, _khz, _since, _waitingFor) = (radio, khz, now, TuneStep.SetStandby);
        return new TuneCommand(TuneStep.SetStandby, radio, khz);
    }

    /// <summary>
    /// Checks the radios: the next command to send (or null), and whether the tune finished now (true: done,
    /// false: the aircraft refused it and auto-tune is now off; null: still going or idle).
    /// </summary>
    public (TuneCommand? Command, bool? Finished) Update(SimState s, TimeSpan now)
    {
        if (_waitingFor is not { } step) return (null, null);
        int active = _radio == 2 ? s.Com2ActiveKhz : s.Com1ActiveKhz;
        int standby = _radio == 2 ? s.Com2StandbyKhz : s.Com1StandbyKhz;
        if (active == _khz)
        {
            _waitingFor = null;
            return (null, true);
        }
        bool late = now - _since > StepTimeout;
        switch (step)
        {
            case TuneStep.SetStandby when standby == _khz:
                return Next(TuneStep.Swap, now);
            case TuneStep.SetStandby when late:
            case TuneStep.Swap when late:
                return Next(TuneStep.SetActive, now); // standby ignored, or the swap did nothing: set active directly
            case TuneStep.SetActive when late:
                _waitingFor = null;
                Disabled = true;
                return (null, false);
            default:
                return (null, null);
        }
    }

    private (TuneCommand?, bool?) Next(TuneStep step, TimeSpan now)
    {
        _waitingFor = step;
        _since = now;
        return (new TuneCommand(step, _radio, _khz), null);
    }
}
