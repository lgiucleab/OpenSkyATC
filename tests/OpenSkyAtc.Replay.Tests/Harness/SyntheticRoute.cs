using OpenSkyAtc.Core.Airspace;
using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;

namespace OpenSkyAtc.Replay.Tests.Harness;

/// <summary>
/// A made-up gate-to-gate world: two airports about 410 nm apart on the same latitude, both with runway 09/27,
/// three route waypoints, and two CENTER frequencies along the way. Wind from 090 so both ends use runway 09
/// and the arrival is straight in along the route. All names and frequencies are fictitious test data.
/// </summary>
public static class SyntheticRoute
{
    public const int OriginDeliveryKhz = 121_850, OriginGroundKhz = 121_700, OriginTowerKhz = 118_100, OriginDepartureKhz = 124_700;
    public const int DestApproachKhz = 120_800, DestTowerKhz = 118_300, DestGroundKhz = 121_900;
    public const int GuardKhz = 121_500;

    public static readonly GeoPoint OriginThreshold = new(40.0, 0.0);
    public static readonly GeoPoint DestinationThreshold = new(40.0, 9.0);

    public static readonly AirportData Origin = Airport("XORG", "Originville", OriginThreshold,
    [
        new AirportFrequency(ControllerRole.Delivery, OriginDeliveryKhz),
        new AirportFrequency(ControllerRole.Ground, OriginGroundKhz),
        new AirportFrequency(ControllerRole.Tower, OriginTowerKhz),
        new AirportFrequency(ControllerRole.Departure, OriginDepartureKhz),
    ]);

    public static readonly AirportData Destination = Airport("XDES", "Destville", DestinationThreshold,
    [
        new AirportFrequency(ControllerRole.Approach, DestApproachKhz),
        new AirportFrequency(ControllerRole.Tower, DestTowerKhz),
        new AirportFrequency(ControllerRole.Ground, DestGroundKhz),
    ]);

    /// <summary>Stand at the origin: 800 m north of the holding point.</summary>
    public static readonly GeoPoint OriginStand = GeoMath.Destination(HoldingPoint(OriginThreshold), 0, 800);

    /// <summary>Stand at the destination: 600 m north of the runway, 1,500 m along it.</summary>
    public static readonly GeoPoint DestinationStand = GeoMath.Destination(GeoMath.Destination(DestinationThreshold, 90, 1500), 0, 600);

    public static readonly FlightPlan Plan = new()
    {
        Callsign = "VLG1234",
        Telephony = "Vueling",
        AircraftType = "A320",
        Origin = "XORG",
        Destination = "XDES",
        CruiseLevel = AltitudeTarget.FlightLevel(240),
        DestinationStand = "12",
        Route =
        [
            new RouteWaypoint("ALFAA", new GeoPoint(40.0, 2.0)),
            new RouteWaypoint("BRAVO", new GeoPoint(40.0, 4.5)),
            new RouteWaypoint("CHRLY", new GeoPoint(40.0, 7.0)),
        ],
    };

    public static readonly IReadOnlyList<CenterFrequency> Centers =
    [
        new CenterFrequency(132_350, new GeoPoint(40.3, 3.0), "XAAA", "ALPHA CENTER"),
        new CenterFrequency(128_500, new GeoPoint(40.3, 6.0), "XBBB", "BRAVO CTR"),
    ];

    public static GeoPoint HoldingPoint(GeoPoint threshold) => GeoMath.Destination(threshold, 0, 60);

    private static AirportData Airport(string icao, string name, GeoPoint threshold, IReadOnlyList<AirportFrequency> frequencies)
    {
        var east = GeoMath.Destination(threshold, 90, 3000);
        return new AirportData
        {
            Icao = icao,
            Name = name,
            ElevationFt = 0,
            Location = threshold,
            Runways = [new Runway(new RunwayEnd("09", threshold, 90), new RunwayEnd("27", east, 270), 45)],
            HoldingPoints = [new HoldingPoint("A1", HoldingPoint(threshold), "09")],
            Frequencies = frequencies,
        };
    }
}
