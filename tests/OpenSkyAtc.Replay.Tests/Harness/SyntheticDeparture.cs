using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Replay.Tests.Harness;

/// <summary>
/// A made-up airport and a deterministic 4 Hz departure from it, used until real recordings exist.
/// The aircraft taxis due north from its stand to holding point A1, lines up on runway 36 and departs.
/// </summary>
/// <remarks>
/// Timeline (seconds): 0 parked on Delivery; 40 tunes Ground; 60-90 pushback; 70/75 engines start;
/// 120-300 taxi at 15 kt; 300-360 stopped at A1; 310 tunes Tower; 360-380 lines up; 430 takeoff roll;
/// about 445 lift-off; about 475 passes 1,000 ft AGL; 490 tunes Departure; ends at 520.
/// </remarks>
public static class SyntheticDeparture
{
    public static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(250);
    public const double FieldElevationFt = 13;

    public const int DeliveryKhz = 121_850, GroundKhz = 121_700, TowerKhz = 118_100, DepartureKhz = 124_700, GuardKhz = 121_500;

    public static readonly GeoPoint Stand = new(41.2900, 2.0700);
    private const double HoldingPointM = 1358, ThresholdM = 1398;

    public static readonly AirportData Origin = new()
    {
        Icao = "XTST",
        Name = "Testville",
        ElevationFt = FieldElevationFt,
        Runways =
        [
            new Runway(
                new RunwayEnd("36", North(ThresholdM), 0),
                new RunwayEnd("18", North(ThresholdM + 3000), 180),
                45),
        ],
        HoldingPoints = [new HoldingPoint("A1", North(HoldingPointM), "36")],
        Frequencies =
        [
            new AirportFrequency(ControllerRole.Delivery, DeliveryKhz),
            new AirportFrequency(ControllerRole.Ground, GroundKhz),
            new AirportFrequency(ControllerRole.Tower, TowerKhz),
            new AirportFrequency(ControllerRole.Departure, DepartureKhz),
        ],
    };

    public static readonly AirportData Destination = new() { Icao = "XDST", Name = "Destville" };

    public static readonly FlightPlan Plan = new()
    {
        Callsign = "VLG1234",
        Telephony = "Vueling",
        AircraftType = "A320",
        Origin = "XTST",
        Destination = "XDST",
        PlannedSid = "TEST1A",
        CruiseLevel = AltitudeTarget.FlightLevel(350),
    };

    public static IReadOnlyList<SimState> Build(int seconds = 520)
    {
        var states = new List<SimState>();
        double northM = 0, groundSpeed = 0, agl = 0, vs = 0;
        bool onGround = true;

        for (int i = 0; i < seconds * 4; i++)
        {
            var t = Tick * i;
            double s = t.TotalSeconds;

            groundSpeed = s switch
            {
                < 60 => 0,
                < 90 => 2,      // pushback (moving backwards, see below)
                < 120 => 0,
                < 300 => 15,
                < 360 => 0,
                < 380 => 5,     // line up
                < 430 => 0,
                _ when onGround => Math.Min(160, groundSpeed + 2.5),
                _ => Math.Min(250, groundSpeed + 0.25),
            };
            if (onGround && groundSpeed >= 150) onGround = false;
            if (!onGround)
            {
                vs = 2000;
                agl += vs / 60 * Tick.TotalSeconds;
            }
            double metres = groundSpeed * 1852 / 3600 * Tick.TotalSeconds;
            northM += s is >= 60 and < 90 ? -metres : metres;
            var pos = North(northM);

            int com1 = s switch
            {
                < 40 => DeliveryKhz,
                < 310 => GroundKhz,
                < 490 => TowerKhz,
                _ => DepartureKhz,
            };

            states.Add(new SimState
            {
                Time = t,
                ZuluTime = new DateTime(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc) + t,
                Latitude = pos.Latitude,
                Longitude = pos.Longitude,
                AltitudeMslFt = FieldElevationFt + agl,
                AltitudeAglFt = agl,
                IndicatedAltitudeFt = FieldElevationFt + agl,
                IndicatedAirspeedKt = groundSpeed,
                GroundSpeedKt = groundSpeed,
                HeadingMagneticDeg = 359,
                HeadingTrueDeg = 0,
                VerticalSpeedFpm = vs,
                OnGround = onGround,
                ParkingBrakeSet = s < 55 || s is >= 95 and < 115,
                EnginesRunning = [s >= 70, s >= 75],
                Com1ActiveKhz = com1,
                Com1StandbyKhz = 122_800,
                Com2ActiveKhz = GuardKhz,
                Com2StandbyKhz = 122_800,
                Com1Transmit = true,
                TransponderCode = 2000,
                TransponderMode = onGround ? TransponderMode.Standby : TransponderMode.Alt,
                GearHandle = agl > 200 ? 0 : 1,
                FlapsHandleIndex = agl > 3000 ? 0 : 1,
                WindDirectionTrueDeg = 350,
                WindSpeedKt = 8,
                VisibilityM = 10_000,
                TemperatureC = 18,
                SeaLevelPressureHpa = 1013,
            });
        }
        return states;
    }

    private static GeoPoint North(double metres) => GeoMath.Destination(Stand, 0, metres);
}
