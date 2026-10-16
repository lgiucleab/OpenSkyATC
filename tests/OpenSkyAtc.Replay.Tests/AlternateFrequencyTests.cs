using OpenSkyAtc.Core.Brain;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Replay.Tests.Harness;
using static OpenSkyAtc.Replay.Tests.Harness.SyntheticDeparture;

namespace OpenSkyAtc.Replay.Tests;

/// <summary>Airports often list several frequencies for one role; a call on any of them reaches the unit.</summary>
public class AlternateFrequencyTests
{
    private const int SecondDeliveryKhz = 130_355;

    [Fact]
    public void ClearanceOnTheSecondDeliveryFrequency_IsAnsweredAndReadBack()
    {
        var origin = Origin with { Frequencies = [.. Origin.Frequencies, new AirportFrequency(ControllerRole.Delivery, SecondDeliveryKhz)] };
        var recording = Build().Select(s => s.Com1ActiveKhz == DeliveryKhz ? s with { Com1ActiveKhz = SecondDeliveryKhz } : s).ToList();

        var brain = new AtcBrain();
        var t = ReplayRunner.Run(brain, recording, IntentScript.Parse("00:10 RequestClearance"), Plan, origin, Destination, seed: 1);

        Assert.Contains(t, l => l.Contains("PILOT (130.355) RequestClearance"));
        Assert.Contains(t, l => l.Contains("ATC Testville Delivery:") && l.Contains("cleared to XDST"));
        Assert.Contains(t, l => l.Contains("ifr-clearance: correct"));
        Assert.DoesNotContain(t, l => l.StartsWith("[") && l.Contains("DROPPED"));
        Assert.Contains(t, l => l.Contains("PHASE Preflight -> Cleared"));
    }

    [Fact]
    public void AFrequencyNoUnitHas_StillReachesNobody()
    {
        var recording = Build().Select(s => s.Com1ActiveKhz == DeliveryKhz ? s with { Com1ActiveKhz = 124_850 } : s).ToList();
        var t = ReplayRunner.Run(new AtcBrain(), recording, IntentScript.Parse("00:10 RequestClearance"), Plan, Origin, Destination, seed: 1);
        Assert.Contains(t, l => l.Contains("PILOT (124.850, no station) RequestClearance"));
        Assert.DoesNotContain(t, l => l.Contains("ATC Testville Delivery"));
    }
}
