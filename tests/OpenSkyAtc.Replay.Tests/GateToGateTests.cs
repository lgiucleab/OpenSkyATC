using OpenSkyAtc.Core.Conformance;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Replay.Tests.Harness;

namespace OpenSkyAtc.Replay.Tests;

/// <summary>Gate-to-gate flights flown by the simulated pilot on the synthetic route (step 6 done-criteria).</summary>
public class GateToGateTests
{
    private static readonly FlightPhase[] AllPhases = Enum.GetValues<FlightPhase>();

    private static List<FlightPhase> PhasesVisited(SimulatedPilot pilot) =>
        pilot.Brain.Session.Log.Entries.OfType<PhaseChanged>().Select(p => p.To).Prepend(FlightPhase.Preflight).ToList();

    /// <summary>On a stuck flight, writes the whole transcript to &lt;name&gt;.received.txt (git-ignored) for inspection.</summary>
    private static string Name([System.Runtime.CompilerServices.CallerMemberName] string test = "") => $"stuck-{test}";

    private static void EndedOrDump(string name, SimulatedPilot pilot, IReadOnlyList<string> transcript)
    {
        if (pilot.Brain.Session.Ended) return;
        string path = Path.Combine(Fixtures.TranscriptsDir, $"{name}.received.txt");
        File.WriteAllLines(path, transcript);
        Assert.Fail($"The flight did not end (phase {pilot.Brain.Session.Phase}). Full transcript: {path}");
    }

    [Fact]
    public void FullFlight_VisitsEveryPhaseInOrder_AndEndsParked()
    {
        var pilot = new SimulatedPilot();
        var transcript = pilot.Fly(seed: 3);

        EndedOrDump(Name(), pilot, transcript);
        Assert.Equal(AllPhases, PhasesVisited(pilot));
        Fixtures.VerifyTranscript("gate-to-gate-seed3", transcript);
    }

    [Fact]
    public void FullFlight_FlownToTheClearance_HasNoConformanceWarnings()
    {
        var pilot = new SimulatedPilot();
        pilot.Fly(seed: 3);
        Assert.Empty(pilot.Brain.Session.Log.Entries.OfType<ConformanceWarning>());
    }

    [Fact]
    public void FullFlight_UsesBothCtrSectors()
    {
        var pilot = new SimulatedPilot();
        var transcript = pilot.Fly(seed: 3);
        Assert.Contains(transcript, l => l.Contains("ATC Alpha Control:"));
        Assert.Contains(transcript, l => l.Contains("ATC Bravo Control:"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GoAround_ReentersApproach_ThenLands(bool reported)
    {
        var pilot = new SimulatedPilot(new SimulatedPilotOptions { GoAroundOnFirstApproach = true, ReportGoAround = reported });
        var transcript = pilot.Fly(seed: 3);

        EndedOrDump(Name(), pilot, transcript);
        var phases = PhasesVisited(pilot);
        int goAround = phases.IndexOf(FlightPhase.Final);
        Assert.Equal(FlightPhase.Approach, phases[goAround + 1]);
        Assert.Equal(2, phases.Count(p => p == FlightPhase.Final));
        Assert.Single(pilot.Brain.Session.Log.Entries.OfType<GoAroundLogged>());
        Fixtures.VerifyTranscript(reported ? "go-around-reported-seed3" : "go-around-detected-seed3", transcript);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(11)]
    [InlineData(42)]
    public void ManySeeds_NeverGetStuck(int seed)
    {
        var pilot = new SimulatedPilot();
        var transcript = pilot.Fly(seed);
        EndedOrDump($"stuck-seed{seed}", pilot, transcript);
        Assert.Equal(AllPhases, PhasesVisited(pilot));
    }
}
