using OpenSkyAtc.Core.Brain;
using OpenSkyAtc.Core.Recording;
using OpenSkyAtc.Replay.Tests.Harness;

namespace OpenSkyAtc.Replay.Tests;

public class ReplayDeterminismTests
{
    private static readonly FlightPlan Plan = new("VLG1234", "LEBL", "LEMD");

    private const string Script = """
        # Synthetic departure: pilot calls at fixed times
        00:30 RequestClearance
        00:45 Readback
        01:10 RequestPushStart
        01:28 RequestTaxi
        04:35 ReadyForDeparture
        """;

    private static IReadOnlyList<string> Replay(int seed, IReadOnlyList<Core.Sim.SimState>? recording = null) =>
        ReplayRunner.Run(new StubAtcBrain(), recording ?? SyntheticFlight.Departure(), IntentScript.Parse(Script), Plan, seed);

    [Fact]
    public void SameSeed_GivesIdenticalTranscript()
    {
        Assert.Equal(Replay(seed: 42), Replay(seed: 42));
    }

    [Fact]
    public void DifferentSeed_ChangesWording()
    {
        var a = Replay(seed: 1);
        var b = Replay(seed: 2);
        Assert.Equal(a.Count, b.Count);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void RecordingRoundTrip_ReplaysIdentically()
    {
        var original = SyntheticFlight.Departure();
        var lines = new[] { RecordingFormat.HeaderLine("Test aircraft", new DateTime(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc)) }
            .Concat(original.Select(RecordingFormat.StateLine))
            .ToArray();

        var (header, parsed) = RecordingFormat.Parse(lines);

        Assert.Equal("Test aircraft", header.Aircraft);
        Assert.Equal(original.Select(RecordingFormat.StateLine), parsed.Select(RecordingFormat.StateLine));
        Assert.Equal(Replay(42, original), Replay(42, parsed));
    }

    [Fact]
    public void SyntheticDeparture_MatchesApprovedTranscript()
    {
        Fixtures.VerifyTranscript("stub-synthetic-departure-seed42", Replay(seed: 42));
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
            Assert.Equal(Replay(7, states), Replay(7, states));
        }
    }
}
