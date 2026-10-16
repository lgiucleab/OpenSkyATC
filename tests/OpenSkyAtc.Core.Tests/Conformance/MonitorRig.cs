using OpenSkyAtc.Core.Airspace;
using OpenSkyAtc.Core.Brain;
using OpenSkyAtc.Core.Conformance;
using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Events;
using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Messages;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Phases;
using OpenSkyAtc.Core.Radio;
using OpenSkyAtc.Core.Random;
using OpenSkyAtc.Core.Sim;
using OpenSkyAtc.Core.Tests.Events;

namespace OpenSkyAtc.Core.Tests.Conformance;

/// <summary>
/// Drives a <see cref="ConformanceMonitor"/> with synthetic states at 4 Hz, sends whatever it queues at once,
/// and records the transmissions and log entries.
/// </summary>
internal sealed class MonitorRig
{
    public const int TowerKhz = 118_100, DepartureKhz = 124_700, GroundKhz = 121_700;

    public static readonly AirportData Airport = TestWorld.Airport with
    {
        Name = "Testville",
        Frequencies =
        [
            new AirportFrequency(ControllerRole.Ground, GroundKhz),
            new AirportFrequency(ControllerRole.Tower, TowerKhz),
            new AirportFrequency(ControllerRole.Departure, DepartureKhz),
        ],
    };

    private readonly IRandom _rng = new SeededRandom(1);
    private readonly AtcBrainOptions _options;

    public MonitorRig(FlightPhase phase, ControllerRole controller = ControllerRole.Departure, FlightPlan? plan = null,
        AirportData? destination = null, ConformanceOptions? options = null)
    {
        _options = new AtcBrainOptions { Conformance = options ?? new ConformanceOptions() };
        Monitor = new ConformanceMonitor(_options.Conformance);
        Airspace = AirspaceModel.Build(Airport, destination ?? Airport);
        Conversation = new ConversationManager(Queue, _rng, Session.Log);
        Plan = plan ?? new FlightPlan { Callsign = "VLG1234", Origin = Airport.Icao, Destination = (destination ?? Airport).Icao };
        Destination = destination ?? Airport;
        Session.Phase = phase;
        Session.CurrentUnit = Airspace.Units.First(u => u.Role == controller);
    }

    public FlightSession Session { get; } = new(1);
    public Clearance Clearance => Session.Clearance;
    public TransmissionQueue Queue { get; } = new();
    public ConversationManager Conversation { get; }
    public AirspaceModel Airspace { get; }
    public ConformanceMonitor Monitor { get; }
    public FlightPlan Plan { get; }
    public AirportData Destination { get; }
    public TimeSpan Now { get; private set; }

    public List<(TimeSpan Time, ControllerUnit Unit, TransmissionPriority Priority, IReadOnlyList<AtcMessagePart> Parts)> Sent { get; } = [];

    public ControllerUnit Unit(ControllerRole role) => Airspace.Units.First(u => u.Role == role);

    public MonitorRig Hold(double seconds, SimState state)
    {
        int ticks = (int)Math.Round(seconds * 4);
        for (int i = 0; i < ticks; i++) Tick(state);
        return this;
    }

    public MonitorRig Tick(SimState state, params SimEvent[] events)
    {
        Session.SimTime = Now;
        var ctx = new PhaseContext(state with { Time = Now }, Session, Clearance, new EventEngine(), Queue, Airspace, _rng)
        {
            NewEvents = events,
            Conversation = Conversation,
            Plan = Plan,
            Origin = Airport,
            Destination = Destination,
            Options = _options,
            Now = Now,
        };
        Monitor.Tick(ctx);
        foreach (var t in Queue.TakeDue(Now))
        {
            Sent.Add((Now, t.Unit, t.Priority, t.Parts));
            t.OnSent?.Invoke();
            Conversation.OnSent(t, Now);
        }
        Now += TimeSpan.FromMilliseconds(250);
        return this;
    }

    /// <summary>Message parts of every transmission, one list per transmission.</summary>
    public List<IReadOnlyList<AtcMessagePart>> Messages => Sent.Select(s => s.Parts).ToList();

    public List<ConformanceWarning> Warnings => Session.Log.Entries.OfType<ConformanceWarning>().ToList();
    public List<ConformanceEpisode> Episodes => Session.Log.Entries.OfType<ConformanceEpisode>().ToList();

    public static SimState Flying(double altitudeFt = 5000, double iasKt = 240) => TestWorld.Flying with
    {
        IndicatedAltitudeFt = altitudeFt,
        AltitudeMslFt = altitudeFt,
        AltitudeAglFt = altitudeFt - 100,
        IndicatedAirspeedKt = iasKt,
        GroundSpeedKt = iasKt,
        VerticalSpeedFpm = 0,
        HeadingMagneticDeg = 90,
        HeadingTrueDeg = 90,
        Com1ActiveKhz = DepartureKhz,
    };

    public static SimState At(SimState s, GeoPoint p) => s with { Latitude = p.Latitude, Longitude = p.Longitude };
}
