using OpenSkyAtc.Core.Brain;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Phraseology;
using OpenSkyAtc.Replay.Tests;
using OpenSkyAtc.Replay.Tests.Harness;
using OpenSkyAtc.Voice;
using static OpenSkyAtc.Replay.Tests.Harness.SyntheticDeparture;

namespace OpenSkyAtc.Voice.Tests;

/// <summary>
/// Step 12 done-when: a replay played as audio. Runs the synthetic departure through the brain, speaks every ATC
/// transmission with its unit's voice and the radio filter, and writes the session to tests/fixtures/audio/*.wav
/// (git-ignored; play it with `voices play`). Runs only where the voices are installed (`voices install`).
/// </summary>
public class ReplayAudioTests
{
    [Fact]
    public void SyntheticDeparture_PlaysAsAudio()
    {
        var store = new VoiceStore();
        var installed = store.Installed();
        if (installed.Count == 0) return; // no voices on this machine: nothing to render

        const int seed = 1;
        var brain = new AtcBrain();
        ReplayRunner.Run(brain, Build(), IntentScript.Parse(DepartureScenarioTests.NormalRequests), Plan, Origin, Destination, seed);
        var messages = brain.Session.Log.Entries.OfType<AtcTransmissionLogged>().Select(e => e.Message).ToList();
        Assert.True(messages.Count >= 8, $"only {messages.Count} transmissions");

        var renderer = new IcaoRenderer(seed);
        var voices = new VoiceAssigner(seed, installed);
        using var tts = new PiperSynthesizer(store);
        var timeline = new List<float>();
        int rate = 0;
        TimeSpan cursor = TimeSpan.Zero, lastTime = TimeSpan.Zero;
        foreach (var (m, i) in messages.Select((m, i) => (m, i)))
        {
            var clip = RadioFilter.Apply(tts.Speak(renderer.Render(m).Speech, voices.VoiceFor(m.Unit.Callsign)), seed + i);
            rate = clip.SampleRate;
            // Real spacing between transmissions, but silences shortened to 2 s so the file is listenable.
            var silence = TimeSpan.FromSeconds(Math.Clamp((m.Time - lastTime).TotalSeconds, 1, 2));
            timeline.AddRange(new float[(int)(silence.TotalSeconds * rate)]);
            timeline.AddRange(clip.Samples);
            lastTime = m.Time;
            cursor += silence + clip.Duration;
        }

        Assert.True(cursor > TimeSpan.FromSeconds(30), $"only {cursor.TotalSeconds:0} s of audio");
        Assert.Equal(messages.Select(m => m.Unit.Callsign).Distinct().Count(), voices.Assigned.Values.Distinct().Count());
        string dir = Path.Combine(Samples.Root, "audio");
        Directory.CreateDirectory(dir);
        Wav.Write(Path.Combine(dir, "departure-normal.wav"), new AudioClip([.. timeline], rate));
    }

    private static class Samples
    {
        public static string Root { get; } = Fixtures.Root;
    }
}
