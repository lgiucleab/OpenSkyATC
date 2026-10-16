using OpenSkyAtc.Core.Model;

namespace OpenSkyAtc.Core.Airspace;

/// <summary>
/// The controller units for the flight. Step 3 builds the origin units; step 6 adds destination units,
/// generated CTR sectors and their volumes.
/// </summary>
public sealed class AirspaceModel
{
    private readonly List<ControllerUnit> _units = [];

    private AirspaceModel() { }

    public ControllerUnit? OriginDelivery { get; private init; }
    public ControllerUnit? OriginGround { get; private init; }
    public ControllerUnit? OriginTower { get; private init; }
    public ControllerUnit? OriginDeparture { get; private init; }
    public ControllerUnit? OriginApproach { get; private init; }

    public IReadOnlyList<ControllerUnit> Units => _units;

    /// <summary>Gives the IFR clearance: Delivery, else Ground, else Tower.</summary>
    public ControllerUnit? ClearanceUnit => OriginDelivery ?? OriginGround ?? OriginTower;

    /// <summary>Handles push and taxi: Ground, else Tower.</summary>
    public ControllerUnit? GroundUnit => OriginGround ?? OriginTower;

    /// <summary>First radar unit after takeoff: Departure, else Approach (design doc fallback).</summary>
    public ControllerUnit? DepartureUnit => OriginDeparture ?? OriginApproach;

    /// <summary>The unit on a frequency, or null when nobody is there. ATIS is not a unit you talk to.</summary>
    public ControllerUnit? UnitOn(int khz) => _units.FirstOrDefault(u => u.FrequencyKhz == khz && u.Role != ControllerRole.Atis);

    public static AirspaceModel Build(AirportData origin, AirportData destination)
    {
        ControllerUnit? Unit(ControllerRole role)
        {
            var f = origin.Frequencies.FirstOrDefault(x => x.Role == role);
            return f is null ? null : new ControllerUnit(role, f.Callsign ?? $"{origin.Name ?? origin.Icao} {RoleName(role)}", f.FrequencyKhz);
        }

        var model = new AirspaceModel
        {
            OriginDelivery = Unit(ControllerRole.Delivery),
            OriginGround = Unit(ControllerRole.Ground),
            OriginTower = Unit(ControllerRole.Tower),
            OriginDeparture = Unit(ControllerRole.Departure),
            OriginApproach = Unit(ControllerRole.Approach),
        };
        foreach (var u in new[] { model.OriginDelivery, model.OriginGround, model.OriginTower, model.OriginDeparture, model.OriginApproach })
        {
            if (u is not null) model._units.Add(u);
        }
        return model;
    }

    public static string RoleName(ControllerRole role) => role switch
    {
        ControllerRole.Atis => "Information",
        ControllerRole.Delivery => "Delivery",
        ControllerRole.Ground => "Ground",
        ControllerRole.Tower => "Tower",
        ControllerRole.Departure => "Departure",
        ControllerRole.Approach => "Approach",
        ControllerRole.Center => "Control",
        _ => role.ToString(),
    };
}
