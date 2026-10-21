using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Recording;

namespace OpenSkyAtc.Host.Tests;

public class AutoResponderTests
{
    private static readonly ControllerUnit Ground = new(ControllerRole.Ground, "Barajas Ground", 121_630);
    private static readonly Readback Taxi = new(new Dictionary<ReadbackItem, string> { [ReadbackItem.Runway] = "36L" }, 4);
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static PilotIntent? At(AutoResponder r, double t, IReadOnlyList<PilotIntent> available, bool atcDone = true, bool free = true, int khz = 121_630) =>
        r.Next(available, atcDone, free, Ground, khz, FlightPhase.TaxiOut, S(t));

    [Fact]
    public void ReadsBack_AfterAPause_Once()
    {
        var r = new AutoResponder();
        PilotIntent[] available = [Taxi, new SayAgain()];
        Assert.Null(At(r, 0, available));
        Assert.Null(At(r, 1.5, available));
        Assert.Same(Taxi, At(r, 2.1, available));
        Assert.Null(At(r, 5, available)); // not twice, even if the brain still lists it
    }

    [Fact]
    public void WaitsWhileATCIsSpeakingOrSomeoneTransmits()
    {
        var r = new AutoResponder();
        PilotIntent[] available = [Taxi];
        Assert.Null(At(r, 0, available, atcDone: false));
        Assert.Null(At(r, 3, available, free: false));
        Assert.Null(At(r, 3.5, available));   // the pause starts once all is quiet
        Assert.Null(At(r, 5, available));
        Assert.Same(Taxi, At(r, 5.6, available));
    }

    [Fact]
    public void ACorrectionAskingForOtherItems_IsAnsweredAgain()
    {
        var r = new AutoResponder();
        At(r, 0, [Taxi]);
        Assert.Same(Taxi, At(r, 2.5, [Taxi]));
        var again = new Readback(new Dictionary<ReadbackItem, string> { [ReadbackItem.HoldShorts] = "36R" }, 4);
        At(r, 3, [again]);
        Assert.Same(again, At(r, 5.5, [again]));
    }

    [Fact]
    public void ChecksIn_OnlyOnTheExpectedFrequency_AndNeverMakesRequests()
    {
        var r = new AutoResponder();
        PilotIntent[] checkIn = [new CheckIn(3000), new RequestTaxi()];
        Assert.Null(At(r, 0, checkIn, khz: 118_100));
        Assert.Null(At(r, 5, checkIn, khz: 118_100));
        At(r, 6, checkIn);
        Assert.IsType<CheckIn>(At(r, 8.5, checkIn));

        var requests = new AutoResponder();
        PilotIntent[] only = [new RequestTaxi(), new RequestPushStart(), new ReadyForDeparture(), new SayAgain()];
        for (int t = 0; t < 20; t++) Assert.Null(At(requests, t, only));
    }

    [Fact]
    public void AnAutoCall_IsMarkedInTheFlightLog_AndStillParses()
    {
        string line = FlightLogFormat.IntentLine(12, Taxi, auto: true);
        Assert.Contains("\"auto\":true", line);
        Assert.DoesNotContain("auto", FlightLogFormat.IntentLine(12, Taxi));
    }
}
