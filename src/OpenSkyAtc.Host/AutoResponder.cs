using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Model;

namespace OpenSkyAtc.Host;

/// <summary>
/// Auto-respond (owner's choice, refactor phase 3): answers ATC for the pilot. It reads back what ATC is waiting
/// for (the readback button's call, so always correct) and checks in on a new frequency. Requests (clearance, push,
/// taxi, ready for departure...) stay the pilot's. It answers like a pilot would: after ATC has finished speaking,
/// with the frequency quiet and the pilot not transmitting, and after a short pause. It answers each readback once
/// (a correction asking for other items is a new one) and checks in once per unit and phase.
/// </summary>
public sealed class AutoResponder
{
    public static readonly TimeSpan Pause = TimeSpan.FromSeconds(2);

    private readonly HashSet<string> _done = [];
    private string? _waiting;
    private TimeSpan _since;

    /// <param name="available">The calls the pilot can make now (the brain's AvailableIntents).</param>
    /// <param name="atcDone">ATC has finished the call a readback is for (the readback countdown runs).</param>
    /// <param name="channelFree">Nobody is talking: the pilot is not transmitting and no ATC call is playing.</param>
    /// <param name="expected">The unit the pilot should be talking to; a check-in goes out only when it is tuned.</param>
    /// <returns>The call to send now, or null.</returns>
    public PilotIntent? Next(IReadOnlyList<PilotIntent> available, bool atcDone, bool channelFree, ControllerUnit? expected,
        int transmitKhz, FlightPhase phase, TimeSpan now)
    {
        PilotIntent? call;
        string key;
        if (available.OfType<Readback>().FirstOrDefault(r => r.Items.Count > 0) is { } readback)
        {
            if (!atcDone) return Reset();
            call = readback;
            key = $"readback:{readback.ExchangeId}:{string.Join(",", readback.Items.OrderBy(i => i.Key).Select(i => $"{i.Key}={i.Value}"))}";
        }
        else if (available.OfType<CheckIn>().FirstOrDefault() is { } checkIn && expected is not null && expected.Answers(transmitKhz))
        {
            call = checkIn;
            key = $"checkin:{expected.Callsign}:{phase}";
        }
        else
        {
            return Reset();
        }

        if (!channelFree || _done.Contains(key)) return Reset();
        if (_waiting != key)
        {
            _waiting = key;
            _since = now;
            return null;
        }
        if (now - _since < Pause) return null;
        _done.Add(key);
        _waiting = null;
        return call;
    }

    private PilotIntent? Reset()
    {
        _waiting = null;
        return null;
    }
}
