using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Sim;
using OpenSkyAtc.Core.Tests.Planning;

namespace OpenSkyAtc.Replay.Tests.Harness;

/// <summary>
/// A taxi-out at the synthetic grid airport XGRD (see <see cref="SyntheticAirports"/>): push back from stand 11,
/// taxi A, B across runway 09/27 (stopping at its hold-short until cleared), then C to the runway 18 holding point.
/// </summary>
/// <remarks>
/// Timeline (seconds): 0 parked on Delivery; 40 tunes Ground; 60-90 pushback south; 120 taxi at 15 kt;
/// stops at the 09/27 hold-short and waits there for <see cref="HoldShortWaitS"/>; then on to the 18 holding point.
/// </remarks>
public static class SyntheticGridTaxi
{
    public static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(250);
    public const double HoldShortWaitS = 45;
    public const int DeliveryKhz = 121_850, GroundKhz = 121_700, TowerKhz = 118_100;

    public static readonly AirportData Origin = FacilityConverter.ToAirportData(SyntheticAirports.Grid());
    public static readonly AirportData Destination = new() { Icao = "XDST", Name = "Destville" };

    public static readonly FlightPlan Plan = new()
    {
        Callsign = "VLG1234",
        Telephony = "Vueling",
        AircraftType = "A320",
        Origin = "XGRD",
        Destination = "XDST",
        PlannedSid = "GRID1A",
        CruiseLevel = AltitudeTarget.FlightLevel(350),
    };

    // The taxi route in metres east/north, from the end of the pushback.
    private static readonly (double E, double N)[] Route = [(1000, 370), (1000, 150), (1500, 150), (1500, 62), (1500, -260)];
    private const int HoldShortIndex = 3;

    public static IReadOnlyList<SimState> Build()
    {
        var states = new List<SimState>();
        var pos = SyntheticAirports.At(1000, 400);
        double heading = 180, speed = 0;
        int leg = 0;
        double? waitUntil = null, tuneTowerAt = null;
        bool done = false;

        for (int i = 0; !done || states.Count < (tuneTowerAt ?? 0) * 4 + 120; i++)
        {
            var t = Tick * i;
            double s = t.TotalSeconds;

            if (s is >= 60 and < 90)
            {
                speed = 2;
                pos = GeoMath.Destination(pos, 180, speed * 1852 / 3600 * Tick.TotalSeconds); // nose north, moving south
                heading = 0;
            }
            else if (s >= 120 && !done && (waitUntil is null || s >= waitUntil))
            {
                speed = 15;
                var target = SyntheticAirports.At(Route[leg + 1].E, Route[leg + 1].N);
                double step = speed * 1852 / 3600 * Tick.TotalSeconds;
                heading = GeoMath.BearingDeg(pos, target);
                if (GeoMath.DistanceM(pos, target) <= step)
                {
                    pos = target;
                    leg++;
                    if (leg == HoldShortIndex && waitUntil is null) { waitUntil = s + HoldShortWaitS; speed = 0; }
                    if (leg == Route.Length - 1) { done = true; speed = 0; tuneTowerAt = s + 20; }
                }
                else pos = GeoMath.Destination(pos, heading, step);
            }
            else speed = 0;

            int com1 = s < 40 ? DeliveryKhz : tuneTowerAt is { } at && s >= at ? TowerKhz : GroundKhz;
            states.Add(new SimState
            {
                Time = t,
                ZuluTime = new DateTime(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc) + t,
                Latitude = pos.Latitude,
                Longitude = pos.Longitude,
                AltitudeMslFt = 328,
                IndicatedAltitudeFt = 328,
                IndicatedAirspeedKt = speed,
                GroundSpeedKt = speed,
                HeadingMagneticDeg = heading,
                HeadingTrueDeg = heading,
                OnGround = true,
                ParkingBrakeSet = s < 55 || s is >= 95 and < 115,
                EnginesRunning = [s >= 70, s >= 75],
                Com1ActiveKhz = com1,
                Com1StandbyKhz = 122_800,
                Com2ActiveKhz = 121_500,
                Com2StandbyKhz = 122_800,
                Com1Transmit = true,
                TransponderCode = 2000,
                TransponderMode = TransponderMode.Standby,
                GearHandle = 1,
                FlapsHandleIndex = 1,
                WindDirectionTrueDeg = 180,
                WindSpeedKt = 8,
                VisibilityM = 10_000,
                TemperatureC = 18,
                SeaLevelPressureHpa = 1013,
            });
        }
        return states;
    }
}
