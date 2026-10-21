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
