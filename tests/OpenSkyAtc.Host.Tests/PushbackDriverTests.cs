using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Planning;
using OpenSkyAtc.Core.Sim;
using OpenSkyAtc.Host;

namespace OpenSkyAtc.Host.Tests;

public class PushbackDriverTests
{
    private sealed class FakeTug : IPushbackTug
    {
        public List<string> Calls { get; } = [];
        public void Start() => Calls.Add("start");
        public void Steer(TugSteer steer) => Calls.Add($"steer {steer}");
        public void Stop() => Calls.Add("stop");
    }

    private static readonly GeoPoint Gate = new(45, 0);
    private static readonly PushPlan Plan = new(Gate, 0, 50, 270, "west");

    private static SimState At(double seconds, double southM, double heading, bool brake = false, int pushback = 0) =>
        new()
        {
            Time = TimeSpan.FromSeconds(seconds),
            Latitude = GeoMath.Destination(Gate, 180, southM).Latitude,
            Longitude = Gate.Longitude,
            HeadingTrueDeg = heading,
            ParkingBrakeSet = brake,
            OnGround = true,
            PushbackState = pushback,
        };

    private static (PushbackDriver Driver, FakeTug Tug, Clearance C) Approved(bool readBack = true)
    {
        var tug = new FakeTug();
        var c = new Clearance { Pushback = Plan, PushbackReadBack = readBack };
        return (new PushbackDriver(tug), tug, c);
    }

    [Fact]
    public void WaitsForTheReadbackAndTheBrake_ThenPushesStraight_Turns_AndStops()
    {
        var (driver, tug, c) = Approved(readBack: false);
        driver.Update(c, FlightPhase.Pushback, At(0, 0, 0, brake: false));
        Assert.Empty(tug.Calls);                             // not read back yet

        c.PushbackReadBack = true;
        driver.Update(c, FlightPhase.Pushback, At(1, 0, 0, brake: true));
        Assert.Empty(tug.Calls);                             // parking brake still set

        driver.Update(c, FlightPhase.Pushback, At(2, 0, 0, pushback: 3));
        Assert.Equal(["start", "steer Straight"], tug.Calls); // tug on, straight back
        Assert.Equal(PushbackStage.Straight, driver.Stage);

        driver.Update(c, FlightPhase.Pushback, At(20, 48, 0));
        Assert.Equal("steer Right", tug.Calls[^1]);         // straight distance reached: nose anticlockwise to 270
        Assert.Equal(PushbackStage.Turning, driver.Stage);

        driver.Update(c, FlightPhase.Pushback, At(30, 55, 300));
        Assert.Equal(PushbackStage.Turning, driver.Stage);
        driver.Update(c, FlightPhase.Pushback, At(35, 58, 271));
        Assert.Equal(["stop", "steer Straight"], tug.Calls[^2..]); // close enough (it keeps turning a little): tug off
        Assert.Equal(PushbackStage.Done, driver.Stage);
    }

    [Fact]
    public void UntilTheSimShowsTheWantedState_TheSteerIsSentAgainEvery2s()
    {
        var (driver, tug, c) = Approved();
        driver.Update(c, FlightPhase.Pushback, At(0, 0, 0, pushback: 3));
        driver.Update(c, FlightPhase.Pushback, At(1, 0, 0, pushback: 1));   // tug attaching, sim turning on its own
        Assert.Single(tug.Calls, x => x == "steer Straight");
        driver.Update(c, FlightPhase.Pushback, At(2.5, 0, 0, pushback: 1));
        Assert.Equal(2, tug.Calls.Count(x => x == "steer Straight"));
        driver.Update(c, FlightPhase.Pushback, At(5, 1, 0, pushback: 0));   // straight now: nothing more
        driver.Update(c, FlightPhase.Pushback, At(9, 3, 0, pushback: 0));
        Assert.Equal(2, tug.Calls.Count(x => x == "steer Straight"));
    }

    [Fact]
    public void SettingTheParkingBrake_StopsThePush()
    {
        var (driver, tug, c) = Approved();
        driver.Update(c, FlightPhase.Pushback, At(0, 0, 0));
        driver.Update(c, FlightPhase.Pushback, At(5, 10, 0, brake: true));
        Assert.Contains("stop", tug.Calls);
        Assert.Equal(PushbackStage.Done, driver.Stage);
    }

    [Fact]
    public void OvershootingTheHeading_TurnsBack()
    {
        var (driver, tug, c) = Approved();
        driver.Update(c, FlightPhase.Pushback, At(0, 0, 0));
        driver.Update(c, FlightPhase.Pushback, At(20, 48, 0));
        driver.Update(c, FlightPhase.Pushback, At(30, 55, 250)); // past 270 the other way
        Assert.Equal("steer Left", tug.Calls[^1]);
    }

    [Fact]
    public void WhenTheSimEndsThePushItself_TheDriverStops()
    {
        var (driver, tug, c) = Approved();
        driver.Update(c, FlightPhase.Pushback, At(0, 0, 0));
        driver.Update(c, FlightPhase.Pushback, At(10, 0, 0, pushback: 3));   // tug still attaching: not judged yet
        Assert.Equal(PushbackStage.Straight, driver.Stage);
        driver.Update(c, FlightPhase.Pushback, At(20, 8, 0, pushback: 3));
        Assert.Equal(PushbackStage.Done, driver.Stage);
    }

    [Fact]
    public void OutsideThePushPhases_NothingHappens()
    {
        var (driver, tug, c) = Approved();
        driver.Update(c, FlightPhase.Holding, At(0, 0, 0));
        Assert.Empty(tug.Calls);
    }

    [Fact]
    public void TaxiAskedBeforeThePush_ThePushStillRunsWhenTheBrakeIsReleased()
    {
        // The 1.0.0 Alpha flight: push read back, brake still set, taxi asked for at the stand.
        var (driver, tug, c) = Approved();
        driver.Update(c, FlightPhase.Pushback, At(0, 0, 0, brake: true));
        Assert.True(driver.WaitingForBrake(c, FlightPhase.Pushback, At(0, 0, 0, brake: true)));
        driver.Update(c, FlightPhase.TaxiOut, At(10, 0, 0, brake: true));
        Assert.True(driver.WaitingForBrake(c, FlightPhase.TaxiOut, At(10, 0, 0, brake: true)));
        Assert.Empty(tug.Calls);

        driver.Update(c, FlightPhase.TaxiOut, At(40, 0, 0, pushback: 3));
        Assert.Equal(["start", "steer Straight"], tug.Calls);
        Assert.False(driver.WaitingForBrake(c, FlightPhase.TaxiOut, At(40, 0, 0)));
    }

    [Fact]
    public void LeavingTheStandUnderOwnPower_CancelsAPushNotStarted()
    {
        var (driver, tug, c) = Approved();
        driver.Update(c, FlightPhase.Pushback, At(0, 0, 0, brake: true));
        driver.Update(c, FlightPhase.TaxiOut, At(30, -20, 0)); // 20 m forward of the stand
        Assert.Equal(PushbackStage.Done, driver.Stage);
        Assert.Empty(tug.Calls);
    }

    [Fact]
    public void Abandon_ForgetsWithoutTouchingTheTug_FinishStopsIt()
    {
        var (driver, tug, c) = Approved();
        driver.Update(c, FlightPhase.Pushback, At(0, 0, 0));
        driver.Abandon();
        Assert.DoesNotContain("stop", tug.Calls);

        var (driver2, tug2, c2) = Approved();
        driver2.Update(c2, FlightPhase.Pushback, At(0, 0, 0));
        driver2.Finish();
        Assert.Contains("stop", tug2.Calls);
    }
}
