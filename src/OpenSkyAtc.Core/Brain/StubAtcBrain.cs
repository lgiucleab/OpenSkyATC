using OpenSkyAtc.Core.Events;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Random;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Core.Brain;

/// <summary>
/// Throwaway brain for the replay harness until the real brain exists (step 3). It runs the event
/// engine, logs everything, and answers a few events and every pilot intent with a wording picked by
/// the seeded RNG, so determinism can be tested end to end.
/// </summary>
public sealed class StubAtcBrain : IAtcBrain
{
    private static readonly string[] Acknowledgements = ["roger", "copied", "understood"];

    private FlightPlan? _plan;
    private IRandom _rng = new SeededRandom(0);
    private EventEngine _events = new();

    public FlightSession Session { get; private set; } = new(0);

    public IReadOnlyList<PilotIntent> AvailableIntents { get; } =
        Enum.GetValues<PilotIntentKind>().Select(k => new PilotIntent(k)).ToArray();

    public event Action<AtcMessage>? Transmission;

    public void Start(FlightPlan plan, AirportData origin, AirportData destination, int seed)
    {
        _plan = plan;
        _rng = new SeededRandom(seed);
        _events = new EventEngine();
        _events.SetAirports(origin, destination);
        Session = new FlightSession(seed);
    }

    public void Tick(SimState state, TimeSpan simTime)
    {
        Session.SimTime = simTime;
        foreach (var e in _events.Process(state))
        {
            Session.Log.Add(new SimEventLogged(e));
            switch (e)
            {
                case Airborne: Say($"airborne, {Pick()}"); break;
                case Touchdown: Say($"touchdown, {Pick()}"); break;
                case FrequencyChanged { Com: 1 } f: Say($"COM1 now {f.FrequencyKhz / 1000.0:0.000}, {Pick()}"); break;
            }
        }
    }

    public void HandlePilotIntent(PilotIntent intent)
    {
        Session.LastPilotTransmission = Session.SimTime;
        Session.Log.Add(new PilotIntentLogged(Session.SimTime, intent));
        Say($"{intent.Kind}, {Pick()}");
    }

    private string Pick() => Acknowledgements[_rng.Next(0, Acknowledgements.Length)];

    private void Say(string text)
    {
        var message = new AtcMessage(Session.SimTime, "STUB", _plan?.Callsign ?? "", [new TextPart(text)]);
        Session.Log.Add(new AtcTransmissionLogged(message));
        Transmission?.Invoke(message);
    }
}
