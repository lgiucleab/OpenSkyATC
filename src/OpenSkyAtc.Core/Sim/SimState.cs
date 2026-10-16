using System.Text.Json.Serialization;

namespace OpenSkyAtc.Core.Sim;

/// <summary>Transponder mode, numbered as the SimConnect TRANSPONDER STATE enum.</summary>
public enum TransponderMode
{
    Off = 0,
    Standby = 1,
    Test = 2,
    On = 3,
    Alt = 4,
    Ground = 5,
}

/// <summary>Engine type, numbered as the SimConnect ENGINE TYPE enum.</summary>
public enum EngineType
{
    Piston = 0,
    Jet = 1,
    None = 2,
    HeloTurbine = 3,
    Unsupported = 4,
    Turboprop = 5,
    Electric = 6,
}

/// <summary>Pause state, bit flags as reported by the SimConnect Pause_EX1 system event.</summary>
[Flags]
public enum PauseFlags
{
    None = 0,
    Full = 1,
    Legacy = 2,
    Active = 4,
    SimOnly = 8,
}

/// <summary>
/// One snapshot of the aircraft and its environment. Produced by the SimConnect bridge at 4 Hz
/// and stored one per line in recordings.
/// </summary>
/// <remarks>
/// Frequencies are integer kHz (121.850 MHz = 121850) so they compare exactly.
/// The transponder code holds its four octal digits as a decimal number (squawk 4521 = 4521).
/// Weather is ambient at the aircraft; MSFS exposes no airport METAR, dewpoint or cloud layers through SimVars.
/// </remarks>
public sealed record SimState
{
    /// <summary>Time since the start of the session or recording. Keeps running while the sim is paused.</summary>
    public TimeSpan Time { get; init; }

    /// <summary>Sim date and time in UTC, from the ZULU environment variables.</summary>
    public DateTime ZuluTime { get; init; }

    public PauseFlags Pause { get; init; }
    public bool SlewActive { get; init; }

    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double AltitudeMslFt { get; init; }
    public double AltitudeAglFt { get; init; }
    public double IndicatedAltitudeFt { get; init; }
    public double IndicatedAirspeedKt { get; init; }
    public double GroundSpeedKt { get; init; }
    public double HeadingMagneticDeg { get; init; }
    public double HeadingTrueDeg { get; init; }
    public double VerticalSpeedFpm { get; init; }

    public bool OnGround { get; init; }
    public bool ParkingBrakeSet { get; init; }

    public EngineType EngineType { get; init; } = EngineType.Jet;

    /// <summary>One entry per engine (at most four), true when that engine is running.</summary>
    public IReadOnlyList<bool> EnginesRunning { get; init; } = [];

    public int Com1ActiveKhz { get; init; }
    public int Com1StandbyKhz { get; init; }
    public int Com2ActiveKhz { get; init; }
    public int Com2StandbyKhz { get; init; }

    /// <summary>Audio panel transmit selection. When neither is set, COM1 is assumed.</summary>
    public bool Com1Transmit { get; init; }
    public bool Com2Transmit { get; init; }

    public int TransponderCode { get; init; }
    public TransponderMode TransponderMode { get; init; }

    /// <summary>Gear handle position, 0 = up, 1 = down.</summary>
    public double GearHandle { get; init; }
    public int FlapsHandleIndex { get; init; }

    /// <summary>Wind at the aircraft, direction relative to true north.</summary>
    public double WindDirectionTrueDeg { get; init; }
    public double WindSpeedKt { get; init; }
    public double VisibilityM { get; init; }
    public double TemperatureC { get; init; }
    public double SeaLevelPressureHpa { get; init; }

    [JsonIgnore]
    public bool IsPaused => Pause != PauseFlags.None;
    [JsonIgnore]
    public bool AnyEngineRunning => EnginesRunning.Any(r => r);

    /// <summary>The active frequency of the radio the pilot transmits on.</summary>
    [JsonIgnore]
    public int TransmitFrequencyKhz => Com2Transmit && !Com1Transmit ? Com2ActiveKhz : Com1ActiveKhz;

    /// <summary>True when either COM radio's active frequency is <paramref name="khz"/>.</summary>
    public bool IsTunedTo(int khz) => Com1ActiveKhz == khz || Com2ActiveKhz == khz;

    /// <summary>COM1 or COM2 is on any of the unit's frequencies.</summary>
    public bool IsTunedTo(Model.ControllerUnit unit) => unit.Answers(Com1ActiveKhz) || unit.Answers(Com2ActiveKhz);
}
