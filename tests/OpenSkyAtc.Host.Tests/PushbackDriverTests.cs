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
        public void Toggle() => Calls.Add("toggle");
        public void Steer(double headingTrueDeg) => Calls.Add($"steer {headingTrueDeg:0}");
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
        Assert.Equal(["toggle", "steer 0"], tug.Calls);      // tug on, straight back
        Assert.Equal(PushbackStage.Straight, driver.Stage);

        driver.Update(c, FlightPhase.Pushback, At(20, 48, 0));
        Assert.Equal("steer 270", tug.Calls[^1]);           // straight distance reached: turn
        Assert.Equal(PushbackStage.Turning, driver.Stage);

        driver.Update(c, FlightPhase.Pushback, At(30, 55, 300));
        Assert.Equal(PushbackStage.Turning, driver.Stage);
        driver.Update(c, FlightPhase.Pushback, At(35, 58, 272));
        Assert.Equal("toggle", tug.Calls[^1]);              // aligned: tug off
        Assert.Equal(PushbackStage.Done, driver.Stage);
    }

    [Fact]
    public void SettingTheParkingBrake_StopsThePush()
    {
        var (driver, tug, c) = Approved();
        driver.Update(c, FlightPhase.Pushback, At(0, 0, 0));
        driver.Update(c, FlightPhase.Pushback, At(5, 10, 0, brake: true));
        Assert.Equal("toggle", tug.Calls[^1]);
        Assert.Equal(2, tug.Calls.Count(x => x == "toggle"));
        Assert.Equal(PushbackStage.Done, driver.Stage);
    }

    [Fact]
    public void WhenTheSimEndsThePushItself_TheDriverStopsWithoutToggling()
    {
        var (driver, tug, c) = Approved();
        driver.Update(c, FlightPhase.Pushback, At(0, 0, 0));
        driver.Update(c, FlightPhase.Pushback, At(5, 5, 0, pushback: 3));    // starting up: not judged yet
        Assert.Equal(PushbackStage.Straight, driver.Stage);
        driver.Update(c, FlightPhase.Pushback, At(10, 8, 0, pushback: 3));
        Assert.Equal(PushbackStage.Done, driver.Stage);
    }

    [Fact]
    public void OutsideThePushPhases_NothingHappens()
    {
        var (driver, tug, c) = Approved();
        driver.Update(c, FlightPhase.TaxiOut, At(0, 0, 0));
        Assert.Empty(tug.Calls);
    }

    [Fact]
    public void Abandon_ForgetsWithoutTouchingTheTug_FinishStopsIt()
    {
        var (driver, tug, c) = Approved();
        driver.Update(c, FlightPhase.Pushback, At(0, 0, 0));
        driver.Abandon();
        Assert.Single(tug.Calls, x => x == "toggle");

        var (driver2, tug2, c2) = Approved();
        driver2.Update(c2, FlightPhase.Pushback, At(0, 0, 0));
        driver2.Finish();
        Assert.Equal(2, tug2.Calls.Count(x => x == "toggle"));
    }
}
