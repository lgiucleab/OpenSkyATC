using OpenSkyAtc.Core.Events;
using OpenSkyAtc.Core.Sim;
using static OpenSkyAtc.Core.Tests.Events.TestWorld;

namespace OpenSkyAtc.Core.Tests.Events;

public class RadioEventTests
{
    [Fact]
    public void FrequencyChange_FiresAfterOneSecond()
    {
        var t = new Timeline()
            .Hold(2, Flying)
            .Hold(2, Flying with { Com1ActiveKhz = 124_700 });

        var e = Assert.Single(t.Of<FrequencyChanged>());
        Assert.Equal(1, e.Com);
        Assert.Equal(124_700, e.FrequencyKhz);
    }

    [Fact]
    public void Com2_IsReportedSeparately()
    {
        var t = new Timeline()
            .Hold(2, Flying)
            .Hold(2, Flying with { Com2ActiveKhz = 118_105 });

        Assert.Equal(2, Assert.Single(t.Of<FrequencyChanged>()).Com);
    }

    [Fact]
    public void SpinningTheKnob_ReportsOnlyTheFinalFrequency()
    {
        var t = new Timeline()
            .Hold(2, Flying)
            .Run(2, s => Flying with { Com1ActiveKhz = 122_000 + (int)(s * 4) * 25 })
            .Hold(2, Flying with { Com1ActiveKhz = 124_700 });

        Assert.Equal(124_700, Assert.Single(t.Of<FrequencyChanged>()).FrequencyKhz);
    }

    [Fact]
    public void MomentaryChange_DoesNotFire()
    {
        var t = new Timeline()
            .Hold(2, Flying)
            .Hold(0.5, Flying with { Com1ActiveKhz = 124_700 })
            .Hold(2, Flying);

        Assert.Empty(t.Of<FrequencyChanged>());
    }

    [Fact]
    public void SquawkChange_FiresAfterTwoSeconds()
    {
        var t = new Timeline()
            .Hold(2, Flying)
            .Hold(3, Flying with { TransponderCode = 4521 });

        var e = Assert.Single(t.Of<SquawkChanged>());
        Assert.Equal(4521, e.Code);
        Assert.Equal(TimeSpan.FromSeconds(4), e.Time);
    }

    [Fact]
    public void DialingThroughCodes_ReportsOnlyTheFinalCode()
    {
        var t = new Timeline()
            .Hold(2, Flying)
            .Hold(1, Flying with { TransponderCode = 4000 })
            .Hold(1, Flying with { TransponderCode = 4500 })
            .Hold(3, Flying with { TransponderCode = 4521 });

        Assert.Equal(4521, Assert.Single(t.Of<SquawkChanged>()).Code);
    }

    [Fact]
    public void TransponderModeChange_Fires()
    {
        var t = new Timeline()
            .Hold(2, Parked)
            .Hold(1, Parked with { TransponderMode = TransponderMode.On })
            .Hold(3, Parked with { TransponderMode = TransponderMode.Alt });

        Assert.Equal(TransponderMode.Alt, Assert.Single(t.Of<TransponderModeChanged>()).Mode);
    }
}
