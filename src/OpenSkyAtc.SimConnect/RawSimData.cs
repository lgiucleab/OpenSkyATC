using System.Globalization;
using System.Runtime.InteropServices;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.SimConnect;

/// <summary>
/// Raw values as SimConnect returns them, in the order of <see cref="Definition"/>.
/// Every field is requested as FLOAT64 to keep marshalling simple.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RawSimData
{
    public double Latitude, Longitude, AltitudeMsl, AltitudeAgl, IndicatedAltitude;
    public double IndicatedAirspeed, GroundSpeed, HeadingMagnetic, HeadingTrue, VerticalSpeed;
    public double OnGround, ParkingBrake, EngineTypeCode, NumberOfEngines, Engine1, Engine2, Engine3, Engine4;
    public double Com1Active, Com1Standby, Com2Active, Com2Standby, Com1Transmit, Com2Transmit, TransponderCode, TransponderState;
    public double GearHandle, FlapsHandleIndex;
    public double WindDirection, WindVelocity, Visibility, Temperature, SeaLevelPressure;
    public double SlewActive, ZuluTimeSeconds, ZuluDayOfYear, ZuluYear;
    public double PushbackState;

    /// <summary>SimVar name and unit for each field, in field order. Names checked against the MSFS 2024 SDK docs.</summary>
    public static readonly (string Name, string Unit)[] Definition =
    [
        ("PLANE LATITUDE", "degrees"),
        ("PLANE LONGITUDE", "degrees"),
        ("PLANE ALTITUDE", "feet"),
        ("PLANE ALT ABOVE GROUND", "feet"),
        ("INDICATED ALTITUDE", "feet"),
        ("AIRSPEED INDICATED", "knots"),
        ("GROUND VELOCITY", "knots"),
        ("PLANE HEADING DEGREES MAGNETIC", "degrees"),
        ("PLANE HEADING DEGREES TRUE", "degrees"),
        ("VERTICAL SPEED", "feet per minute"),
        ("SIM ON GROUND", "bool"),
        ("BRAKE PARKING POSITION", "bool"),
        ("ENGINE TYPE", "Enum"),
        ("NUMBER OF ENGINES", "number"),
        ("ENG COMBUSTION:1", "bool"),
        ("ENG COMBUSTION:2", "bool"),
        ("ENG COMBUSTION:3", "bool"),
        ("ENG COMBUSTION:4", "bool"),
        ("COM ACTIVE FREQUENCY:1", "KHz"),
        ("COM STANDBY FREQUENCY:1", "KHz"),
        ("COM ACTIVE FREQUENCY:2", "KHz"),
        ("COM STANDBY FREQUENCY:2", "KHz"),
        ("COM TRANSMIT:1", "bool"),
        ("COM TRANSMIT:2", "bool"),
        ("TRANSPONDER CODE:1", "Bco16"),
        ("TRANSPONDER STATE:1", "Enum"),
        ("GEAR HANDLE POSITION", "percent over 100"),
        ("FLAPS HANDLE INDEX", "number"),
        ("AMBIENT WIND DIRECTION", "degrees"),
        ("AMBIENT WIND VELOCITY", "knots"),
        ("AMBIENT VISIBILITY", "meters"),
        ("AMBIENT TEMPERATURE", "celsius"),
        ("SEA LEVEL PRESSURE", "millibars"),
        ("IS SLEW ACTIVE", "bool"),
        ("ZULU TIME", "seconds"),
        ("ZULU DAY OF YEAR", "number"),
        ("ZULU YEAR", "number"),
        ("PUSHBACK STATE", "Enum"),
    ];

    /// <summary>Converts raw values into a <see cref="SimState"/>.</summary>
    public readonly SimState ToSimState(TimeSpan time, PauseFlags pause)
    {
        int engines = Math.Clamp((int)NumberOfEngines, 0, 4);
        bool[] running = new[] { Engine1, Engine2, Engine3, Engine4 }.Take(engines).Select(v => v != 0).ToArray();

        return new SimState
        {
            Time = time,
            ZuluTime = ToZulu(ZuluYear, ZuluDayOfYear, ZuluTimeSeconds),
            Pause = pause,
            SlewActive = SlewActive != 0,
            PushbackState = (int)PushbackState,
            Latitude = Latitude,
            Longitude = Longitude,
            AltitudeMslFt = AltitudeMsl,
            AltitudeAglFt = AltitudeAgl,
            IndicatedAltitudeFt = IndicatedAltitude,
            IndicatedAirspeedKt = IndicatedAirspeed,
            GroundSpeedKt = GroundSpeed,
            HeadingMagneticDeg = HeadingMagnetic,
            HeadingTrueDeg = HeadingTrue,
            VerticalSpeedFpm = VerticalSpeed,
            OnGround = OnGround != 0,
            ParkingBrakeSet = ParkingBrake != 0,
            EngineType = Enum.IsDefined((EngineType)(int)EngineTypeCode) ? (EngineType)(int)EngineTypeCode : EngineType.Unsupported,
            EnginesRunning = running,
            Com1ActiveKhz = (int)Math.Round(Com1Active),
            Com1StandbyKhz = (int)Math.Round(Com1Standby),
            Com2ActiveKhz = (int)Math.Round(Com2Active),
            Com2StandbyKhz = (int)Math.Round(Com2Standby),
            Com1Transmit = Com1Transmit != 0,
            Com2Transmit = Com2Transmit != 0,
            TransponderCode = DecodeBco16(TransponderCode),
            TransponderMode = Enum.IsDefined((TransponderMode)(int)TransponderState) ? (TransponderMode)(int)TransponderState : TransponderMode.Off,
            GearHandle = GearHandle,
            FlapsHandleIndex = (int)FlapsHandleIndex,
            WindDirectionTrueDeg = WindDirection,
            WindSpeedKt = WindVelocity,
            VisibilityM = Visibility,
            TemperatureC = Temperature,
            SeaLevelPressureHpa = SeaLevelPressure,
        };
    }

    /// <summary>BCO16 holds one octal digit per hex nibble: 0x4521 is squawk 4521.</summary>
    public static int DecodeBco16(double raw) =>
        int.Parse(((int)raw).ToString("X4", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private static DateTime ToZulu(double year, double dayOfYear, double seconds)
    {
        if (year < 1 || year > 9999 || dayOfYear < 1) return DateTime.MinValue;
        return new DateTime((int)year, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(dayOfYear - 1).AddSeconds(seconds);
    }
}

/// <summary>Aircraft title, requested separately because it is a string.</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
public struct RawTitle
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string Title;
}
