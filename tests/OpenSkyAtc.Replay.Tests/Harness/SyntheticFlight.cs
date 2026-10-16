using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Replay.Tests.Harness;

/// <summary>
/// Generates simple deterministic recordings at 4 Hz, used until real recorded flights exist.
/// The aircraft moves along a straight line; values are plausible, not physically exact.
/// </summary>
public static class SyntheticFlight
{
    public static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Parked 60 s with engines off, engines on, taxi 180 s at 15 kt, takeoff roll, climb to 5,000 ft,
    /// then a COM1 change (handoff to departure) at about 1,000 ft AGL.
    /// </summary>
    public static IReadOnlyList<SimState> Departure()
    {
        const double fieldElevationFt = 13;
        var states = new List<SimState>();
        double distanceNm = 0, altitudeAgl = 0, groundSpeed = 0, verticalSpeed = 0;
        bool onGround = true;
        int com1 = 121_700;

        for (int i = 0; i < 4 * 600; i++)
        {
            var t = Tick * i;
            double s = t.TotalSeconds;
            bool engines = s >= 60;

            if (s < 90) groundSpeed = 0;
            else if (s < 270) groundSpeed = 15;
            else if (s < 280) groundSpeed = 0;
            else if (onGround) groundSpeed = Math.Min(160, groundSpeed + 2.5);
            else groundSpeed = Math.Min(250, groundSpeed + 0.25);

            if (onGround && groundSpeed >= 150) onGround = false;
            if (!onGround)
            {
                verticalSpeed = altitudeAgl < 5000 - fieldElevationFt ? 2000 : 0;
                altitudeAgl = Math.Min(5000 - fieldElevationFt, altitudeAgl + verticalSpeed / 60 * Tick.TotalSeconds);
            }
            if (!onGround && altitudeAgl >= 1000) com1 = 124_700;

            distanceNm += groundSpeed * Tick.TotalHours;

            states.Add(new SimState
            {
                Time = t,
                ZuluTime = new DateTime(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc) + t,
                Latitude = 41.2900 + distanceNm / 60,
                Longitude = 2.0700,
                AltitudeMslFt = fieldElevationFt + altitudeAgl,
                AltitudeAglFt = altitudeAgl,
                IndicatedAltitudeFt = fieldElevationFt + altitudeAgl,
                IndicatedAirspeedKt = groundSpeed,
                GroundSpeedKt = groundSpeed,
                HeadingMagneticDeg = 359,
                HeadingTrueDeg = 0,
                VerticalSpeedFpm = verticalSpeed,
                OnGround = onGround,
                ParkingBrakeSet = s < 85,
                EnginesRunning = [engines, engines],
                Com1ActiveKhz = com1,
                Com1StandbyKhz = 118_105,
                Com2ActiveKhz = 121_500,
                Com2StandbyKhz = 122_800,
                TransponderCode = 4521,
                TransponderMode = onGround ? TransponderMode.Standby : TransponderMode.Alt,
                GearHandle = altitudeAgl > 200 ? 0 : 1,
                FlapsHandleIndex = altitudeAgl > 3000 ? 0 : 1,
                WindDirectionTrueDeg = 250,
                WindSpeedKt = 12,
                VisibilityM = 10_000,
                TemperatureC = 18,
                SeaLevelPressureHpa = 1013,
            });
        }
        return states;
    }
}
