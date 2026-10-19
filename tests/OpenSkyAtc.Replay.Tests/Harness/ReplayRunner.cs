using System.Globalization;
using OpenSkyAtc.Core.Brain;
using OpenSkyAtc.Core.Conformance;
using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Sim;
using OpenSkyAtc.Phraseology;

namespace OpenSkyAtc.Replay.Tests.Harness;

public sealed record ReplayOptions
{
    /// <summary>Simulated pilot: read back every instruction correctly after <see cref="AutoReadbackDelay"/>.</summary>
    public bool AutoReadback { get; init; } = true;

    public TimeSpan AutoReadbackDelay { get; init; } = TimeSpan.FromSeconds(4);

    public PhraseologyOptions? Phraseology { get; init; }

    /// <summary>Simulated pilot: sets the transponder to the assigned squawk (the recording cannot know it in advance).</summary>
    public bool SetsSquawk { get; init; } = true;
}

/// <summary>
/// Feeds a recording into a brain as fast as possible (no wall-clock waits), applies scripted pilot
/// intents when their time is reached, and returns the transcript built from the session log.
/// </summary>
public static class ReplayRunner
{
    public static IReadOnlyList<string> Run(
        IAtcBrain brain,
        IReadOnlyList<SimState> recording,
        IReadOnlyList<ScriptedIntent> script,
        FlightPlan plan,
        AirportData origin,
        AirportData destination,
        int seed,
        ReplayOptions? options = null,
        Action<IAtcBrain, SimState>? afterTick = null)
    {
        options ??= new ReplayOptions();
        brain.Start(plan, origin, destination, seed);

        int next = 0;
        TimeSpan? autoReadbackAt = null;
        foreach (var state in recording)
        {
            var s = options.SetsSquawk && brain.Session.Clearance.Squawk is { } code ? state with { TransponderCode = code } : state;

            // Tick first so the brain's clock is at this sample when the pilot speaks.
            brain.Tick(s, s.Time);
            afterTick?.Invoke(brain, s);

            while (next < script.Count && script[next].Time <= state.Time)
                brain.HandlePilotIntent(IntentFactory.Create(script[next++], brain.AvailableIntents));

            if (!options.AutoReadback) continue;
            bool readbackPending = brain.AvailableIntents.OfType<Readback>().Any();
            if (!readbackPending) autoReadbackAt = null;
            else if (autoReadbackAt is null) autoReadbackAt = state.Time + options.AutoReadbackDelay;
            else if (autoReadbackAt <= state.Time)
            {
                brain.HandlePilotIntent(brain.AvailableIntents.OfType<Readback>().First());
                autoReadbackAt = null;
            }
        }
        return Transcript(brain.Session, new IcaoRenderer(seed, options.Phraseology));
    }

    /// <summary>
    /// Pilot calls, ATC transmissions (display text), phase changes and readback verdicts, one line each.
    /// ATC messages go through <paramref name="renderer"/> in order, as in the app.
    /// </summary>
    public static IReadOnlyList<string> Transcript(FlightSession session, IcaoRenderer renderer)
    {
        var droppedRenderer = new IcaoRenderer(session.Seed, new PhraseologyOptions { EuropeanVariants = false, Courtesy = false });
        var lines = new List<string>();
        foreach (var entry in session.Log.Entries)
        {
            string t = entry.Time.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
            string? line = entry switch
            {
                PilotIntentLogged p => $"PILOT ({Spoken.DisplayFrequency(p.FrequencyKhz)}{(p.Unit is null ? ", no station" : "")}) {IntentFactory.Describe(p.Intent)}",
                AtcTransmissionLogged a => $"ATC {a.Message.Unit.Callsign}: {renderer.Render(a.Message).Display}",
                PhaseChanged c => $"PHASE {c.From} -> {c.To} ({c.Reason})",
                ReadbackChecked { Verdict.IsCorrect: true } r => $"READBACK #{r.ExchangeId} {r.Tag}: correct",
                ReadbackChecked r => $"READBACK #{r.ExchangeId} {r.Tag}: wrong [{string.Join(",", r.Verdict.Wrong)}] missing [{string.Join(",", r.Verdict.Missing)}]",
                ReadbackMissed m => $"READBACK #{m.ExchangeId} {m.Tag}: missed",
                TransmissionDropped d => $"DROPPED {d.Unit.Callsign}: {string.Join(", ", d.Parts.Select(x => droppedRenderer.RenderPart(x).Display))}",
                RequestRepeated r => $"REPEATED {IntentFactory.Describe(r.Intent)}: {r.Tag}",
                RequestRejected r => $"REJECTED {IntentFactory.Describe(r.Intent)}: {r.Reason}",
                HandoffSkipped h => $"HANDOFF SKIPPED: expected {h.Expected.Callsign}, called {h.Called.Callsign}",
                ConformanceWarning w => $"CONFORMANCE {w.Check} level {w.Level}{(w.Serious ? " (serious)" : "")}",
                ConformanceEpisode e => $"CONFORMANCE {e.Check} ended: level {e.MaxLevel}, worst {e.Worst}{e.Unit}, {(e.IsDeviation ? "deviation" : "warning")}",
                _ => null,
            };
            if (line is not null) lines.Add($"[{t}] {line}");
        }
        return lines;
    }
}
