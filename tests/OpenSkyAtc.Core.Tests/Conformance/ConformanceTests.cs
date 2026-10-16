using OpenSkyAtc.Core.Conformance;
using OpenSkyAtc.Core.Events;
using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Messages;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Radio;
using OpenSkyAtc.Core.Sim;
using OpenSkyAtc.Core.Tests.Events;
using static OpenSkyAtc.Core.Tests.Conformance.MonitorRig;

namespace OpenSkyAtc.Core.Tests.Conformance;

public class AltitudeBustTests
{
    private static MonitorRig Cleared120()
    {
        var rig = new MonitorRig(FlightPhase.Departure);
        rig.Clearance.ClearedAltitude = AltitudeTarget.FlightLevel(120);
        rig.Hold(31, Flying(11_000)); // climbing towards FL120; past the 30 s suppression
        return rig;
    }

    [Fact]
    public void Bust_EscalatesThroughThreeLevels_ThenStops()
    {
        var rig = Cleared120().Hold(120, Flying(12_400));

        Assert.Equal([1, 2, 3], rig.Warnings.Select(w => w.Level));
        var msgs = rig.Messages;
        Assert.Equal<AtcMessagePart>([new CheckAltitude(AltitudeTarget.FlightLevel(120))], msgs[0]);
        Assert.Equal<AtcMessagePart>([new DescendTo(AltitudeTarget.FlightLevel(120), Immediately: true)], msgs[1]);
        Assert.Equal(2, msgs.Count); // level 3 is a log entry only, and nothing after it

        // Level 1 after the 5 s grace, then 15 s apart (decision 1).
        var times = rig.Warnings.Select(w => w.Time.TotalSeconds).ToArray();
        Assert.Equal(5, times[0] - 31, 0.3);
        Assert.Equal(15, times[1] - times[0], 0.3);
        Assert.Equal(15, times[2] - times[1], 0.3);
    }

    [Fact]
    public void WithinTolerance_NoWarning()
    {
        var rig = Cleared120().Hold(60, Flying(12_290));
        Assert.Empty(rig.Warnings);
    }

    [Fact]
    public void ReturningInsideTolerance_ResetsTheLevel_AndLogsTheEpisode()
    {
        var rig = Cleared120()
            .Hold(8, Flying(12_400))     // level 1
            .Hold(5, Flying(12_000))     // back on level: episode ends
            .Hold(8, Flying(12_500));    // a new episode starts at level 1 again

        Assert.Equal([1, 1], rig.Warnings.Select(w => w.Level));
        var ep = Assert.Single(rig.Episodes);
        Assert.Equal(ConformanceCheck.AltitudeBust, ep.Check);
        Assert.Equal(1, ep.MaxLevel);
        Assert.Equal(400, ep.Worst, 0);
        Assert.False(ep.IsDeviation);
    }

    [Fact]
    public void NewClearance_SuppressesAltitudeChecksFor30Seconds()
    {
        var rig = new MonitorRig(FlightPhase.Departure);
        rig.Hold(1, Flying(11_000));
        rig.Clearance.ClearedAltitude = AltitudeTarget.FlightLevel(120); // a climb clearance, given at 11,000 ft
        rig.Hold(1, Flying(11_000)).Hold(28, Flying(12_500));
        Assert.Empty(rig.Warnings);
        rig.Hold(7, Flying(12_500));
        Assert.Single(rig.Warnings);
    }

    [Fact]
    public void OnlyThePastSideCounts_WhenClimbing()
    {
        var rig = Cleared120().Hold(60, Flying(9_000)); // well below, but climbing towards it
        Assert.DoesNotContain(rig.Warnings, w => w.Check == ConformanceCheck.AltitudeBust);
    }

    [Fact]
    public void BelowWhenDescending_IsABust_AndTheCorrectionIsAClimb()
    {
        var rig = new MonitorRig(FlightPhase.Departure);
        rig.Hold(1, Flying(9_000));
        rig.Clearance.ClearedAltitude = AltitudeTarget.Feet(5000);
        rig.Hold(31, Flying(6_000)).Hold(25, Flying(4_500));

        Assert.Contains<AtcMessagePart>(new ClimbTo(AltitudeTarget.Feet(5000), Immediately: true), rig.Messages.SelectMany(m => m));
    }
}

public class AltitudeNotComplyingTests
{
    [Fact]
    public void NoClimbAfter60s_ConfirmsThenRepeatsTheInstruction()
    {
        var rig = new MonitorRig(FlightPhase.Departure);
        rig.Hold(1, Flying(20_000));
        rig.Clearance.ClearedAltitude = AltitudeTarget.FlightLevel(240);
        rig.Hold(200, Flying(20_000));

        var nc = rig.Warnings.Where(w => w.Check == ConformanceCheck.AltitudeNotComplying).ToList();
        Assert.Equal([1, 2, 3], nc.Select(w => w.Level));
        Assert.Equal(60, nc[0].Time.TotalSeconds - 1, 0.3);
        Assert.Equal(60, nc[1].Time.TotalSeconds - nc[0].Time.TotalSeconds, 0.3);
        Assert.Equal<AtcMessagePart>([new ConfirmVertical(true, AltitudeTarget.FlightLevel(240))], rig.Messages[0]);
        Assert.Equal<AtcMessagePart>([new ClimbTo(AltitudeTarget.FlightLevel(240))], rig.Messages[1]);
    }

    [Theory]
    [InlineData(600, 0)]    // climbing at 600 fpm
    [InlineData(0, 400)]    // already 400 ft higher
    public void StartingTheClimb_Complies(double vsFpm, double movedFt)
    {
        var rig = new MonitorRig(FlightPhase.Departure);
        rig.Hold(1, Flying(20_000));
        rig.Clearance.ClearedAltitude = AltitudeTarget.FlightLevel(240);
        rig.Hold(30, Flying(20_000)).Hold(1, Flying(20_000 + movedFt) with { VerticalSpeedFpm = vsFpm }).Hold(90, Flying(20_000 + movedFt));

        Assert.DoesNotContain(rig.Warnings, w => w.Check == ConformanceCheck.AltitudeNotComplying);
    }

    [Fact]
    public void ClearanceGivenOnTheGround_CountsFromLiftOff()
    {
        var rig = new MonitorRig(FlightPhase.Holding, ControllerRole.Tower);
        rig.Clearance.ClearedAltitude = AltitudeTarget.Feet(5000);
        rig.Hold(120, TestWorld.Parked);                              // waiting at the gate is not "not complying"
        rig.Hold(10, Flying(200) with { VerticalSpeedFpm = 2000 });
        Assert.Empty(rig.Warnings);
    }
}

public class HeadingTests
{
    private static MonitorRig Assigned(int heading, double currentMag)
    {
        var rig = new MonitorRig(FlightPhase.Departure);
        rig.Hold(1, Flying() with { HeadingMagneticDeg = currentMag });
        rig.Clearance.AssignedHeadingDeg = heading;
        return rig;
    }

    [Fact]
    public void StillTurning_IsNotChecked_UntilWithin20OrAfter60s()
    {
        var rig = Assigned(270, 90).Hold(59, Flying() with { HeadingMagneticDeg = 90 });
        Assert.Empty(rig.Warnings);

        rig.Hold(20, Flying() with { HeadingMagneticDeg = 90 }); // 60 s passed, still 180 off: armed, then 15 s grace
        Assert.Equal(ConformanceCheck.Heading, Assert.Single(rig.Warnings).Check);
    }

    [Fact]
    public void DriftingOffAfterReachingTheHeading_Escalates_WithTheShortestTurn()
    {
        var rig = Assigned(270, 90)
            .Hold(5, Flying() with { HeadingMagneticDeg = 265 })    // reached: armed
            .Hold(40, Flying() with { HeadingMagneticDeg = 300 });  // 30 off to the right

        Assert.Equal([1, 2], rig.Warnings.Select(w => w.Level));
        Assert.Equal<AtcMessagePart>([new CheckHeading(), new TurnHeading(TurnDirection.None, 270)], rig.Messages[0]);
        Assert.Equal<AtcMessagePart>([new TurnHeading(TurnDirection.Left, 270, Immediately: true)], rig.Messages[1]);
    }

    [Fact]
    public void WithinTwentyDegrees_IsFine()
    {
        var rig = Assigned(270, 270).Hold(60, Flying() with { HeadingMagneticDeg = 285 });
        Assert.Empty(rig.Warnings);
    }
}

public class OffRouteTests
{
    private static readonly GeoPoint West = new(41.0, 1.0), East = new(41.0, 3.0);

    private static readonly FlightPlan Plan = new()
    {
        Callsign = "VLG1234",
        Origin = "TEST",
        Destination = "TEST",
        Route = [new RouteWaypoint("WEST", West), new RouteWaypoint("EAST", East)],
    };

    private static SimState NorthOfRoute(double nm) =>
        At(Flying(30_000), GeoMath.Destination(new GeoPoint(41.0, 2.0), 0, nm * GeoMath.MetresPerNm));

    [Fact]
    public void MoreThan3nmOff_SaysIntentions_ThenVectorsBack()
    {
        var rig = new MonitorRig(FlightPhase.EnRoute, ControllerRole.Departure, Plan);
        rig.Hold(65, NorthOfRoute(5));

        Assert.Equal<AtcMessagePart>([new SayIntentions()], rig.Messages[0]);
        // Leg course is about 090 true; north of an eastbound leg means turning right to a 120 intercept.
        var vector = Assert.IsType<TurnHeading>(Assert.Single(rig.Messages[1]));
        Assert.Equal(TurnDirection.Right, vector.Direction);
        Assert.InRange(vector.HeadingDeg, 115, 125);
        Assert.Equal(vector.HeadingDeg, rig.Clearance.AssignedHeadingDeg); // the route check hands over to the heading check
    }

    [Fact]
    public void Within3nm_OrInDeparture_OrOnAHeading_IsNotChecked()
    {
        Assert.Empty(new MonitorRig(FlightPhase.EnRoute, plan: Plan).Hold(60, NorthOfRoute(2.5)).Warnings);
        Assert.Empty(new MonitorRig(FlightPhase.Departure, plan: Plan).Hold(60, NorthOfRoute(8)).Warnings);

        var vectored = new MonitorRig(FlightPhase.EnRoute, plan: Plan);
        vectored.Clearance.AssignedHeadingDeg = 90;
        vectored.Hold(60, NorthOfRoute(8));
        Assert.DoesNotContain(vectored.Warnings, w => w.Check == ConformanceCheck.OffRoute);
    }
}

public class SpeedTests
{
    [Fact]
    public void Above250BelowFL100_AsksToReduce()
    {
        var rig = new MonitorRig(FlightPhase.Departure).Hold(25, Flying(8_000, 270));
        Assert.Equal<AtcMessagePart>([new Speed(250, SpeedAction.Reduce)], Assert.Single(rig.Messages));
        Assert.Equal(20, rig.Warnings[0].Time.TotalSeconds, 0.3);
    }

    [Fact]
    public void AboveFL100_NoLimit_ButAssignedSpeedHas15ktTolerance()
    {
        Assert.Empty(new MonitorRig(FlightPhase.EnRoute).Hold(30, Flying(12_000, 300)).Warnings);

        var rig = new MonitorRig(FlightPhase.EnRoute);
        rig.Clearance.SpeedRestrictionKt = 220;
        rig.Hold(59, Flying(12_000, 300));   // just assigned: time to slow down
        Assert.Empty(rig.Warnings);
        rig.Hold(30, Flying(12_000, 230));
        Assert.Empty(rig.Warnings);
        rig.Hold(25, Flying(12_000, 240));
        Assert.Equal<AtcMessagePart>([new Speed(220, SpeedAction.Reduce)], Assert.Single(rig.Messages));
    }
}

public class SquawkTests
{
    [Theory]
    [InlineData(2000, TransponderMode.Alt)]
    [InlineData(4521, TransponderMode.On)]
    public void WrongCodeOrMode_Airborne_SquawkThenRecycle(int code, TransponderMode mode)
    {
        var rig = new MonitorRig(FlightPhase.Departure);
        rig.Clearance.Squawk = 4521;
        rig.Hold(65, Flying() with { TransponderCode = code, TransponderMode = mode });

        Assert.Equal<AtcMessagePart>([new Squawk(4521)], rig.Messages[0]);
        Assert.Equal<AtcMessagePart>([new Squawk(4521, Recycle: true)], rig.Messages[1]);
    }

    [Fact]
    public void OnTheGround_IsNotChecked()
    {
        var rig = new MonitorRig(FlightPhase.TaxiOut, ControllerRole.Ground);
        rig.Clearance.Squawk = 4521;
        rig.Hold(60, TestWorld.Parked);
        Assert.Empty(rig.Warnings);
    }
}

public class MissedHandoffTests
{
    private static MonitorRig HandedOffToDeparture(int com2Khz)
    {
        var rig = new MonitorRig(FlightPhase.InitialClimb, ControllerRole.Tower);
        rig.Clearance.SetHandoff(rig.Unit(Model.ControllerRole.Tower), rig.Unit(Model.ControllerRole.Departure));
        rig.Hold(100, Flying() with { Com1ActiveKhz = TowerKhz, Com2ActiveKhz = com2Khz });
        return rig;
    }

    [Fact]
    public void Repeats_OnTheOldFrequency_ThenCallsOnGuard()
    {
        var rig = HandedOffToDeparture(121_500);

        Assert.Equal([1, 2], rig.Warnings.Select(w => w.Level));
        Assert.Equal(45, rig.Warnings[0].Time.TotalSeconds, 0.3);
        Assert.Equal(TowerKhz, rig.Sent[0].Unit.FrequencyKhz);
        Assert.Equal(121_500, rig.Sent[1].Unit.FrequencyKhz);
        Assert.All(rig.Messages, m => Assert.Equal<AtcMessagePart>([new Contact(rig.Unit(Model.ControllerRole.Departure))], m));
    }

    [Fact]
    public void GuardNotMonitored_GoesStraightToLostComms()
    {
        var rig = HandedOffToDeparture(122_800);
        Assert.Equal([1, 3], rig.Warnings.Select(w => w.Level));
        Assert.Single(rig.Messages);
    }

    [Fact]
    public void TuningTheNewFrequency_EndsIt()
    {
        var rig = new MonitorRig(FlightPhase.InitialClimb, ControllerRole.Tower);
        rig.Clearance.SetHandoff(rig.Unit(Model.ControllerRole.Tower), rig.Unit(Model.ControllerRole.Departure));
        rig.Hold(30, Flying() with { Com1ActiveKhz = TowerKhz }).Hold(60, Flying() with { Com1ActiveKhz = DepartureKhz });
        Assert.Empty(rig.Warnings);
        Assert.Empty(rig.Episodes); // fixed within the grace time: not a warning
    }
}

public class RunwaySafetyTests
{
    private static readonly SimState Taxiing = TestWorld.Parked with { EnginesRunning = [true, true], GroundSpeedKt = 10, Com1ActiveKhz = TowerKhz };

    [Fact]
    public void Incursion_StopImmediately_AtOnce_LoggedSerious()
    {
        var rig = new MonitorRig(FlightPhase.TaxiOut, ControllerRole.Ground);
        rig.Tick(Taxiing, new PassedHoldShort(TimeSpan.Zero, TestWorld.Runway0927, TestWorld.HoldShort));
        rig.Hold(3, Taxiing).Hold(2, Taxiing with { GroundSpeedKt = 0 });

        var sent = Assert.Single(rig.Sent);
        Assert.Equal(TransmissionPriority.Urgent, sent.Priority);
        Assert.Equal(TowerKhz, sent.Unit.FrequencyKhz); // from the unit the pilot is on
        Assert.Equal<AtcMessagePart>([new StopImmediately()], sent.Parts);
        var ep = Assert.Single(rig.Episodes);
        Assert.True(ep.Serious);
        Assert.True(ep.IsDeviation);
    }

    [Fact]
    public void CrossingWithClearance_IsFine()
    {
        var rig = new MonitorRig(FlightPhase.TaxiOut, ControllerRole.Ground);
        rig.Clearance.ClearToCross("09");
        rig.Tick(Taxiing, new PassedHoldShort(TimeSpan.Zero, TestWorld.Runway0927, TestWorld.HoldShort)).Hold(3, Taxiing);
        Assert.Empty(rig.Warnings);
    }

    [Fact]
    public void TakeoffWithoutClearance_StopIsaySgainStop()
    {
        var rolling = Taxiing with { GroundSpeedKt = 45 };
        var rig = new MonitorRig(FlightPhase.Holding, ControllerRole.Tower);
        rig.Tick(rolling, new TakeoffRoll(TimeSpan.Zero, TestWorld.Runway0927)).Hold(15, rolling);

        Assert.Equal<AtcMessagePart>([new StopImmediately(Emphasis: true)], Assert.Single(rig.Messages));
        Assert.Equal(TransmissionPriority.Urgent, rig.Sent[0].Priority);
    }

    [Fact]
    public void TakeoffWithClearance_IsFine()
    {
        var rig = new MonitorRig(FlightPhase.Holding, ControllerRole.Tower);
        rig.Clearance.TakeoffCleared = "09";
        rig.Tick(Taxiing with { GroundSpeedKt = 45 }, new TakeoffRoll(TimeSpan.Zero, TestWorld.Runway0927));
        Assert.Empty(rig.Warnings);
    }

    private static SimState Final(double nm, double offsetDeg = 0, double agl = 150) =>
        At(Flying(agl + 100) with
        {
            AltitudeAglFt = agl,
            HeadingTrueDeg = 90,
            Com1ActiveKhz = TowerKhz,
        }, GeoMath.Destination(TestWorld.Runway0927.First.Position, 270 + offsetDeg, nm * GeoMath.MetresPerNm));

    [Fact]
    public void LowOnFinalWithoutLandingClearance_GoAround_Repeated()
    {
        var rig = new MonitorRig(FlightPhase.Final, ControllerRole.Tower);
        rig.Hold(12, Final(1));

        Assert.Equal(2, rig.Messages.Count);
        Assert.Equal<AtcMessagePart>([new GoAround()], rig.Messages[0]);
        Assert.Equal<AtcMessagePart>([new GoAround(Repeated: true)], rig.Messages[1]);
        Assert.Equal(10, (rig.Sent[1].Time - rig.Sent[0].Time).TotalSeconds, 0.3);
    }

    [Theory]
    [InlineData(1, 0, 150, true)]      // cleared to land
    [InlineData(1, 20, 150, false)]    // 20 degrees off the centreline
    [InlineData(1, 0, 400, false)]     // above 200 ft
    [InlineData(4, 0, 150, false)]     // further than 2 nm
    public void LandingCheck_DoesNotFire(double nm, double offsetDeg, double agl, bool cleared)
    {
        var rig = new MonitorRig(FlightPhase.Final, ControllerRole.Tower);
        if (cleared) rig.Clearance.LandingCleared = "09";
        rig.Hold(12, Final(nm, offsetDeg, agl));
        Assert.Empty(rig.Warnings);
    }

    [Fact]
    public void ClimbOutAfterTakeoff_IsNotAFinal()
    {
        var rig = new MonitorRig(FlightPhase.InitialClimb, ControllerRole.Tower);
        rig.Hold(12, Final(1));
        Assert.Empty(rig.Warnings);
    }
}

public class TaxiOffRouteTests
{
    private static readonly GeoPoint Start = new(41.02, 2.0);

    private static MonitorRig Taxi()
    {
        var rig = new MonitorRig(FlightPhase.TaxiOut, ControllerRole.Ground);
        rig.Clearance.Runway = "09";
        rig.Clearance.Taxi = new TaxiClearance("A1", "09", ["K"], [],
            [Start, GeoMath.Destination(Start, 180, 1000)]);
        return rig;
    }

    private static SimState EastOfPath(double metres) =>
        At(TestWorld.Parked with { GroundSpeedKt = 10, Com1ActiveKhz = GroundKhz }, GeoMath.Destination(GeoMath.Destination(Start, 180, 500), 90, metres));

    [Fact]
    public void MoreThan50mOff_HoldPosition_ThenReRoute()
    {
        var rig = Taxi().Hold(30, EastOfPath(80));
        Assert.Equal<AtcMessagePart>([new HoldPosition()], rig.Messages[0]);
        Assert.IsType<TaxiVia>(Assert.Single(rig.Messages[1]));
    }

    [Fact]
    public void Within50m_OrNoKnownRoute_IsNotChecked()
    {
        Assert.Empty(Taxi().Hold(30, EastOfPath(40)).Warnings);

        var fallback = Taxi();
        fallback.Clearance.Taxi = fallback.Clearance.Taxi! with { Path = [] };
        Assert.Empty(fallback.Hold(30, EastOfPath(300)).Warnings);
    }
}

public class MonitorRuleTests
{
    [Fact]
    public void TwoChecksOut_AreSeparateTransmissions()
    {
        var rig = new MonitorRig(FlightPhase.Departure);
        rig.Clearance.Squawk = 4521;
        rig.Hold(35, Flying(8_000, 270) with { TransponderCode = 2000 });

        Assert.Equal(2, rig.Messages.Count);
        Assert.All(rig.Messages, m => Assert.Single(m));
    }
}
