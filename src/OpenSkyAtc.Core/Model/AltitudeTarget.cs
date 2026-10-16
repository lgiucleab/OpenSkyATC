namespace OpenSkyAtc.Core.Model;

public enum AltitudeKind
{
    Altitude,
    FlightLevel,
}

/// <summary>
/// An altitude with its reference. <see cref="Value"/> is feet for <see cref="AltitudeKind.Altitude"/>
/// and the flight level number (120 = FL120) for <see cref="AltitudeKind.FlightLevel"/>.
/// </summary>
public readonly record struct AltitudeTarget(int Value, AltitudeKind Kind)
{
    public static AltitudeTarget Feet(int feet) => new(feet, AltitudeKind.Altitude);
    public static AltitudeTarget FlightLevel(int level) => new(level, AltitudeKind.FlightLevel);

    /// <summary>Nominal height in feet (flight levels are hundreds of feet on standard pressure).</summary>
    public int NominalFeet => Kind == AltitudeKind.FlightLevel ? Value * 100 : Value;

    public override string ToString() => Kind == AltitudeKind.FlightLevel ? $"FL{Value:000}" : $"{Value} ft";
}
