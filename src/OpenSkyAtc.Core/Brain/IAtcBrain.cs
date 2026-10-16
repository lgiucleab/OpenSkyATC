using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Core.Brain;

public interface IAtcBrain
{
    void Start(FlightPlan plan, AirportData origin, AirportData destination, int seed);
    void Tick(SimState state, TimeSpan simTime);          // called at 4 Hz
    void HandlePilotIntent(PilotIntent intent);           // from buttons or speech
    IReadOnlyList<PilotIntent> AvailableIntents { get; }  // drives the UI buttons
    event Action<AtcMessage> Transmission;                 // to phraseology + TTS
    FlightSession Session { get; }                        // read-only for UI/debrief
}
