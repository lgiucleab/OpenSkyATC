using System.Globalization;
using OpenSkyAtc.Core.Brain;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Replay.Tests.Harness;

/// <summary>
/// Feeds a recording into a brain as fast as possible (no wall-clock waits), applies scripted pilot
/// intents when their time is reached, and returns the transcript.
/// </summary>
public static class ReplayRunner
{
    public static IReadOnlyList<string> Run(
        IAtcBrain brain,
        IReadOnlyList<SimState> recording,
        IReadOnlyList<ScriptedIntent> script,
        FlightPlan plan,
        int seed)
    {
        var transcript = new List<string>();
        brain.Transmission += OnTransmission;
        try
        {
            brain.Start(plan, new AirportData { Icao = plan.Origin }, new AirportData { Icao = plan.Destination }, seed);

            int nextIntent = 0;
            foreach (var state in recording)
            {
                // Tick first so the brain's clock is at this sample when the intent arrives.
                brain.Tick(state, state.Time);
                while (nextIntent < script.Count && script[nextIntent].Time <= state.Time)
                {
                    var intent = script[nextIntent++].Intent;
                    transcript.Add($"[{Format(state.Time)}] PILOT {intent.Kind}");
                    brain.HandlePilotIntent(intent);
                }
            }
        }
        finally
        {
            brain.Transmission -= OnTransmission;
        }
        return transcript;

        void OnTransmission(AtcMessage m) =>
            transcript.Add($"[{Format(m.Time)}] ATC {m.Unit}: {string.Join(" ", m.Parts.Select(Render))}");
    }

    // Step 4 replaces this with the phraseology renderer.
    private static string Render(AtcMessagePart part) => part switch
    {
        TextPart t => t.Text,
        _ => part.ToString(),
    };

    private static string Format(TimeSpan t) => t.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
}
