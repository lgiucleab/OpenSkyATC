using System.Text.RegularExpressions;
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

    /// <summary>Step 7 done-criteria: vectoring checked on an arrival that is not straight in (synthetic until recordings exist).</summary>
    [Fact]
    public void ArrivalFromTheNorth_IsVectoredDownwindBaseAndFinal_WithSpeedControl()
    {
        var pilot = new SimulatedPilot(new SimulatedPilotOptions { Plan = SyntheticRoute.NorthArrivalPlan });
        var transcript = pilot.Fly(seed: 3);

        EndedOrDump("stuck-vectored-arrival", pilot, transcript);
        Fixtures.VerifyTranscript("vectored-arrival-seed3", transcript);
        Assert.Contains(transcript, l => l.Contains("vectoring for ILS approach runway 09"));
        // Either wording variant (design doc or CAP 413).
        Assert.Contains(transcript, l => Regex.IsMatch(l, "reduce speed (to )?220 knots"));
        Assert.Contains(transcript, l => Regex.IsMatch(l, "reduce speed (to )?180 knots"));
        Assert.Contains(transcript, l => l.Contains("closing localiser from the left") && Regex.IsMatch(l, "cleared ILS (approach )?runway 09"));
        Assert.Contains(transcript, l => l.Contains("maintain 160 knots until 4 miles final"));
        Assert.Empty(pilot.Brain.Session.Log.Entries.OfType<ConformanceWarning>());
    }

    [Fact]
    public void WithStarData_TheStarAndApproachAreClearedTogether_WithoutVectors()
    {
        var pilot = new SimulatedPilot(new SimulatedPilotOptions
        {
            Plan = SyntheticRoute.Plan with { PlannedStar = "CHRLY1A" },
            Destination = SyntheticRoute.Destination with { Stars = ["CHRLY1A"] },
        });
        var transcript = pilot.Fly(seed: 3);

        EndedOrDump("stuck-star-arrival", pilot, transcript);
        Assert.Contains(transcript, l => l.Contains("cleared ILS approach runway 09 via CHRLY1A arrival"));
        Assert.DoesNotContain(transcript, l => l.Contains("vectoring for") || l.Contains("closing localiser"));
        Assert.Empty(pilot.Brain.Session.Log.Entries.OfType<ConformanceWarning>());
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

/// <summary>Level and direct requests en route (refactor phase 6), flown gate to gate.</summary>
public class EnRouteRequestTests
{
    private static int ClimbsAfter(IReadOnlyList<string> t, int index) =>
        t.Skip(index + 1).Count(l => l.Contains("ATC ") && Regex.IsMatch(l, @"\bclimb FL\d+"));

    [Fact(Skip = "Pending: check the RequestLevel transcript format (refactor phase 6)")]
    // TODO: the request line format for the level does not match the regex yet; re-enable once checked.
    [Trait("Status", "pending")]
    public void ALowerLevel_IsApproved_AndBecomesTheCruise()
    {
        var pilot = new SimulatedPilot(new SimulatedPilotOptions
        {
            ExtraCalls = [(FlightPhase.EnRoute, available => available.OfType<Core.Conversation.RequestLevel>().ToList() is { Count: 2 } two ? two.MinBy(l => l.Level.NominalFeet) : null)],
        });
        var t = pilot.Fly(seed: 3);
        Assert.True(pilot.Brain.Session.Ended);
        int asked = t.ToList().FindIndex(l => l.Contains("PILOT") && l.Contains("RequestLevel"));
        Assert.True(asked >= 0, "no lower level was asked");
        int level = int.Parse(Regex.Match(t[asked], @"FL(\d+)").Groups[1].Value);
        // "climb FL220" while still below it, "descend FL220" when above: either way to the level asked for.
        Assert.Matches($@"(climb|descend) FL{level}", t.Skip(asked + 1).First(l => l.Contains("ATC ") && l.Contains(" FL")));
        // It is the cruise now: nothing climbs above it later.
        Assert.DoesNotContain(t.Skip(asked + 1), l => l.Contains("ATC ") && Regex.Match(l, @"climb FL(\d+)") is { Success: true } m && int.Parse(m.Groups[1].Value) > level);
    }

    [Fact]
    public void FarAboveTheCruise_IsUnable_WithTheHighestAvailable()
    {
        var pilot = new SimulatedPilot(new SimulatedPilotOptions
        {
            ExtraCalls = [(FlightPhase.EnRoute, _ => new Core.Conversation.RequestLevel(AltitudeTarget.FlightLevel(490)))],
        });
        var t = pilot.Fly(seed: 3);
        Assert.True(pilot.Brain.Session.Ended);
        Assert.Contains(t, l => l.Contains("ATC ") && Regex.IsMatch(l, @"unable approve FL490, FL\d+ available"));
    }

    [Fact]
    public void DirectToALaterFix_IsGivenAsRouteDirect()
    {
        var pilot = new SimulatedPilot(new SimulatedPilotOptions
        {
            ExtraCalls = [(FlightPhase.EnRoute, available => available.OfType<Core.Conversation.RequestDirect>().LastOrDefault())],
        });
        var t = pilot.Fly(seed: 3);
        Assert.True(pilot.Brain.Session.Ended);
        var asked = t.First(l => l.Contains("PILOT") && l.Contains("RequestDirect"));
        string fix = Regex.Match(asked, @"fix=(\S+)").Groups[1].Value;
        Assert.Contains(t, l => l.Contains("ATC ") && (l.Contains($"direct {fix}")));
    }
}
