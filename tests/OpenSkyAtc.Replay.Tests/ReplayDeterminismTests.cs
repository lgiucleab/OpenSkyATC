using OpenSkyAtc.Core.Brain;
using OpenSkyAtc.Core.Recording;
using OpenSkyAtc.Replay.Tests.Harness;
using static OpenSkyAtc.Replay.Tests.DepartureScenarioTests;

namespace OpenSkyAtc.Replay.Tests;

public class ReplayDeterminismTests
{
    [Fact]
    public void SameSeed_GivesIdenticalTranscript()
    {
        Assert.Equal(RunScenario(NormalRequests, seed: 42), RunScenario(NormalRequests, seed: 42));
    }

    [Fact]
    public void DifferentSeed_ChangesTheSession()
    {
        Assert.NotEqual(RunScenario(NormalRequests, seed: 1), RunScenario(NormalRequests, seed: 2));
    }

    [Fact]
    public void RecordingRoundTrip_ReplaysIdentically()
    {
        var original = SyntheticDeparture.Build();
        var lines = new[] { RecordingFormat.HeaderLine("Test aircraft", new DateTime(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc)) }
            .Concat(original.Select(RecordingFormat.StateLine))
            .ToArray();

        var (header, parsed) = RecordingFormat.Parse(lines);

        Assert.Equal("Test aircraft", header.Aircraft);
        Assert.Equal(original.Select(RecordingFormat.StateLine), parsed.Select(RecordingFormat.StateLine));
        Assert.Equal(Run(original), Run(parsed));

        static IReadOnlyList<string> Run(IReadOnlyList<Core.Sim.SimState> rec) =>
            ReplayRunner.Run(new AtcBrain(), rec, IntentScript.Parse(NormalRequests),
                SyntheticDeparture.Plan, SyntheticDeparture.Origin, SyntheticDeparture.Destination, 7);
    }

    [Fact]
    public void RecordedFlights_ReplayDeterministically()
    {
        // Covers every real recording placed in tests/fixtures/recordings; passes when there are none yet.
        if (!Directory.Exists(Fixtures.RecordingsDir)) return;

        foreach (var file in Directory.EnumerateFiles(Fixtures.RecordingsDir, "*.jsonl"))
        {
            var (_, states) = RecordingFormat.Parse(File.ReadLines(file));
            Assert.True(states.Count > 0, $"{file} has no samples.");
            Assert.Equal(Run(states), Run(states));
        }

        static IReadOnlyList<string> Run(IReadOnlyList<Core.Sim.SimState> rec) =>
            ReplayRunner.Run(new AtcBrain(), rec, [], SyntheticDeparture.Plan, SyntheticDeparture.Origin, SyntheticDeparture.Destination, 7);
    }
}
