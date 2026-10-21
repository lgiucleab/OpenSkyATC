using OpenSkyAtc.Core.Atis;
using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Random;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Core.Tests.Atis;

public class AtisTests
{
    private static readonly GeoPoint Field = new(40.47, -3.56);

    private static readonly AirportData Airport = new()
    {
        Icao = "LEMD",
        Name = "Barajas",
        Location = Field,
        TransitionLevel = 70,
        Runways = [new Runway(new RunwayEnd("36L", Field, 0, "ILS"), new RunwayEnd("18R", new GeoPoint(40.50, -3.56), 180, "ILS"), 60)],
    };

    private static readonly ControllerUnit Unit = new(ControllerRole.Atis, "Barajas Information", 118_250);

    private static SimState Weather(double qnh = 1013.4, double windDir = 10, double windKt = 8, double nmAway = 0) => new()
    {
        Latitude = GeoMath.Destination(Field, 90, nmAway * 1852).Latitude,
        Longitude = GeoMath.Destination(Field, 90, nmAway * 1852).Longitude,
        HeadingTrueDeg = 0,
        HeadingMagneticDeg = 0,
        SeaLevelPressureHpa = qnh,
        WindDirectionTrueDeg = windDir,
        WindSpeedKt = windKt,
        VisibilityM = 20_000,
        TemperatureC = 14.4,
        ZuluTime = new DateTime(2026, 10, 20, 10, 20, 0, DateTimeKind.Utc),
    };

    private static readonly RunwayEnd North = Airport.Runways[0].First, South = Airport.Runways[0].Second;

    [Fact]
    public void TheReport_HoldsTheWeatherRunwayAndTransitionLevel()
    {
        var station = new AtisStation(Unit, Airport, arrival: true, new SeededRandom(1));
        station.Update(Weather(), North);
        var r = station.Current!;
        Assert.Equal(("36L", "ILS", 70, 1013, 14), (r.Runway, r.ApproachType, r.TransitionLevel, r.Qnh, r.TemperatureC));
        Assert.Equal(new Messages.Wind(10, 8), r.Wind);
        Assert.False(station.Changed); // the first report is not a change
    }

    [Fact]
    public void TheLetter_MovesOnWithRunwayQnhOrAWindShift_NotOnSmallChanges()
    {
        var station = new AtisStation(Unit, Airport, arrival: false, new SeededRandom(1));
        station.Update(Weather(), North);
        char first = station.Letter;

        station.Update(Weather(qnh: 1013.9, windDir: 30, windKt: 12), North);   // same hPa, small wind change
        Assert.Equal(first, station.Letter);

        station.Update(Weather(qnh: 1012.6), North);                             // QNH 1013 -> 1012
        Assert.True(station.Changed);
        char second = station.Letter;
        Assert.NotEqual(first, second);

        station.Update(Weather(qnh: 1012.6), South);                             // runway change
        Assert.True(station.Changed);
        station.Update(Weather(qnh: 1012.6, windDir: 10, windKt: 25), South);    // wind 8 -> 25 kt
        Assert.True(station.Changed);
        Assert.Null(station.Current!.ApproachType);                              // departure ATIS: runway in use only
    }

    [Fact]
    public void AfterZulu_ComesAlpha()
    {
        var station = new AtisStation(Unit, Airport, arrival: false, new SeededRandom(1));
        station.Update(Weather(), North);
        for (int i = 0; i < 26; i++) station.Update(Weather(qnh: 1000 + (i % 2) * 5), North);
        Assert.InRange(station.Letter, 'A', 'Z');
        var seen = new HashSet<char>();
        for (int i = 0; i < 30; i++)
        {
            station.Update(Weather(qnh: 1000 + (i % 2) * 5), North);
            seen.Add(station.Letter);
        }
        Assert.Equal(26, seen.Count);
    }

    [Fact]
    public void TheBroadcast_IsHeardOnlyOnItsFrequency_AndWithin60Nm()
    {
        var atis = new AtisService(Unit, Airport, null, Airport, new SeededRandom(1));
        atis.Update(Weather(), _ => North);
        Assert.NotNull(atis.On(118_250, Weather()));
        Assert.Null(atis.On(121_700, Weather()));
        Assert.NotNull(atis.On(118_250, Weather(nmAway: 55)));
        Assert.Null(atis.On(118_250, Weather(nmAway: 65)));
    }

    [Fact]
    public void UpHigh_TheReportStays_TheWindAloftDoesNotChangeTheLetter()
    {
        var atis = new AtisService(Unit, Airport, null, Airport, new SeededRandom(1));
        atis.Update(Weather() with { OnGround = true }, _ => North);
        char letter = atis.Stations[0].Letter;
        var aloft = Weather(windDir: 270, windKt: 60) with { OnGround = false, AltitudeAglFt = 8000 };
        Assert.Empty(atis.Update(aloft, _ => South));
        Assert.Equal(letter, atis.Stations[0].Letter);
        var low = Weather(qnh: 1009) with { OnGround = false, AltitudeAglFt = 800 };
        Assert.Single(atis.Update(low, _ => North));   // near the ground it follows the weather again
    }
}
