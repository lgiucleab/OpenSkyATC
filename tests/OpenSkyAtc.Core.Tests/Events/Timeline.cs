using OpenSkyAtc.Core.Events;
using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Core.Tests.Events;

/// <summary>Feeds snapshots into an <see cref="EventEngine"/> at 4 Hz and collects the events.</summary>
internal sealed class Timeline(EventEngine engine)
{
    public static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(250);

    public EventEngine Engine { get; } = engine;
    public TimeSpan Now { get; private set; }
    public List<SimEvent> Events { get; } = [];

    public Timeline() : this(new EventEngine()) { }

    /// <summary>Feeds the same state for the given number of seconds.</summary>
    public Timeline Hold(double seconds, SimState state) => Run(seconds, _ => state);

    /// <summary>Feeds states produced from the elapsed seconds within this segment.</summary>
    public Timeline Run(double seconds, Func<double, SimState> at)
    {
        int ticks = (int)Math.Round(seconds / Tick.TotalSeconds);
        for (int i = 0; i < ticks; i++) Step(at(i * Tick.TotalSeconds));
        return this;
    }

    /// <summary>Feeds one tick per state.</summary>
    public Timeline Steps(IEnumerable<SimState> states)
    {
        foreach (var s in states) Step(s);
        return this;
    }

    public Timeline Step(SimState state)
    {
        Events.AddRange(Engine.Process(state with { Time = Now }));
        Now += Tick;
        return this;
    }

    public List<T> Of<T>() where T : SimEvent => Events.OfType<T>().ToList();
}

/// <summary>Shared states and geometry for event tests.</summary>
internal static class TestWorld
{
    /// <summary>Runway 09/27: 3,000 m long, 45 m wide, west end at 41N 2E, running due east.</summary>
    public static readonly Runway Runway0927 = MakeRunway();

    /// <summary>Hold-short for runway 09/27, 60 m north of the centreline, 500 m from the west end.</summary>
    public static readonly HoldShortPoint HoldShort = new(At(500, 60), "09");

    public static readonly AirportData Airport = new()
    {
        Icao = "TEST",
        Runways = [Runway0927],
        HoldShorts = [HoldShort],
    };

    public static readonly SimState Parked = new()
    {
        Latitude = 41.01,
        Longitude = 2.0,
        OnGround = true,
        ParkingBrakeSet = true,
        EnginesRunning = [false, false],
        Com1ActiveKhz = 121_700,
        Com2ActiveKhz = 121_500,
        TransponderCode = 2000,
        TransponderMode = TransponderMode.Standby,
    };

    public static readonly SimState Flying = Parked with
    {
        OnGround = false,
        ParkingBrakeSet = false,
        EnginesRunning = [true, true],
        AltitudeMslFt = 5000,
        IndicatedAltitudeFt = 5000,
        AltitudeAglFt = 4900,
        IndicatedAirspeedKt = 250,
        GroundSpeedKt = 250,
        TransponderMode = TransponderMode.Alt,
    };

    /// <summary>A point relative to the runway's west end: metres east along the centreline and metres north of it.</summary>
    public static GeoPoint At(double eastM, double northM) =>
        GeoMath.Destination(GeoMath.Destination(Runway0927.First.Position, 90, eastM), 0, northM);

    public static SimState Position(this SimState s, GeoPoint p) => s with { Latitude = p.Latitude, Longitude = p.Longitude };

    private static Runway MakeRunway()
    {
        var west = new GeoPoint(41.0, 2.0);
        var east = GeoMath.Destination(west, 90, 3000);
        return new Runway(new RunwayEnd("09", west, 90), new RunwayEnd("27", east, 270), 45);
    }
}
