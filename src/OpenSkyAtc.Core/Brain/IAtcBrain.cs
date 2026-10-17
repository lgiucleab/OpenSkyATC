using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Messages;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Core.Brain;

public interface IAtcBrain
{
    void Start(FlightPlan plan, AirportData origin, AirportData destination, int seed);
    void Tick(SimState state, TimeSpan simTime);          // called at 4 Hz
    void HandlePilotIntent(PilotIntent intent);           // from buttons or speech

    /// <summary>
    /// The voice layer finished playing <paramref name="message"/> (step 12): the readback timer of its exchange
    /// restarts now, so a long spoken clearance still leaves the pilot the full time to read it back. Never called
    /// in text-only mode, where the timer runs from the moment the message is shown.
    /// </summary>
    void TransmissionHeard(AtcMessage message);
    IReadOnlyList<PilotIntent> AvailableIntents { get; }  // drives the UI buttons
    event Action<AtcMessage> Transmission;                 // to phraseology + TTS
    FlightSession Session { get; }                        // read-only for UI/debrief
}
