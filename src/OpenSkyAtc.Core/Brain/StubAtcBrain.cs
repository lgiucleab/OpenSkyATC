using OpenSkyAtc.Core.Random;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Core.Brain;

/// <summary>
/// Throwaway brain for the step 1 replay harness. It reacts to a few raw state changes and to
/// pilot intents, picking wordings with the seeded RNG, so determinism can be tested before the
/// real brain exists. Replaced by the real brain in step 3.
/// </summary>
public sealed class StubAtcBrain : IAtcBrain
{
    private static readonly string[] Acknowledgements = ["roger", "copied", "understood"];

    private FlightPlan? _plan;
    private IRandom _rng = new SeededRandom(0);
    private SimState? _last;

    public FlightSession Session { get; private set; } = new(0);

    public IReadOnlyList<PilotIntent> AvailableIntents { get; } =
        Enum.GetValues<PilotIntentKind>().Select(k => new PilotIntent(k)).ToArray();

    public event Action<AtcMessage>? Transmission;

    public void Start(FlightPlan plan, AirportData origin, AirportData destination, int seed)
    {
        _plan = plan;
        _rng = new SeededRandom(seed);
        _last = null;
        Session = new FlightSession(seed);
    }

    public void Tick(SimState state, TimeSpan simTime)
    {
        Session.SimTime = simTime;
        if (_last is not null)
        {
            if (_last.OnGround && !state.OnGround) Say($"airborne, {Pick()}");
            if (!_last.OnGround && state.OnGround) Say($"touchdown, {Pick()}");
            if (_last.Com1ActiveKhz != state.Com1ActiveKhz) Say($"COM1 now {state.Com1ActiveKhz / 1000.0:0.000}, {Pick()}");
        }
        _last = state;
    }

    public void HandlePilotIntent(PilotIntent intent) => Say($"{intent.Kind}, {Pick()}");

    private string Pick() => Acknowledgements[_rng.Next(0, Acknowledgements.Length)];

    private void Say(string text) =>
        Transmission?.Invoke(new AtcMessage(Session.SimTime, "STUB", _plan?.Callsign ?? "", [new TextPart(text)]));
}
