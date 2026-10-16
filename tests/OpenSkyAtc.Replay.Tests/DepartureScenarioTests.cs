using OpenSkyAtc.Core.Brain;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Planning;
using OpenSkyAtc.Core.Sim;
using OpenSkyAtc.Replay.Tests.Harness;
using static OpenSkyAtc.Replay.Tests.Harness.SyntheticDeparture;

namespace OpenSkyAtc.Replay.Tests;

/// <summary>
/// Gate-to-departure replays (phases 1-7) on the synthetic departure. Each test approves its transcript;
/// see tests/fixtures/transcripts.
/// </summary>
public class DepartureScenarioTests
{
    private static readonly IReadOnlyList<SimState> Recording = Build();

    /// <summary>The pilot's requests for a normal departure; readbacks come from the auto-readback pilot.</summary>
    public const string NormalRequests = """
        00:10 RequestClearance
        00:45 RequestPushStart
        01:40 RequestTaxi
        05:15 ReadyForDeparture
        08:15 CheckIn
        """;

    internal static IReadOnlyList<string> RunScenario(string script, int seed, ReplayOptions? options = null, AtcBrainOptions? brain = null) =>
        ReplayRunner.Run(new AtcBrain(brain), Recording, IntentScript.Parse(script), Plan, Origin, Destination, seed, options);

    private static readonly ReplayOptions Manual = new() { AutoReadback = false };

    [Fact]
    public void NormalDeparture_DirectTakeoffClearance()
    {
        var t = RunScenario(NormalRequests, seed: 1);
        Fixtures.VerifyTranscript("departure-normal-seed1", t);
        Assert.Contains(t, l => l.Contains("PHASE InitialClimb -> Departure"));
        Assert.DoesNotContain(t, l => l.Contains("line up and wait"));
    }

    [Fact]
    public void NormalDeparture_LineUpAndWaitFirst()
    {
        var t = RunScenario(NormalRequests, seed: 1, brain: new AtcBrainOptions { LineUpAndWaitProbability = 1 });
        Fixtures.VerifyTranscript("departure-lineup-seed1", t);
        int lineUp = t.ToList().FindIndex(l => l.Contains("line up and wait"));
        int takeoff = t.ToList().FindIndex(l => l.Contains("cleared for takeoff"));
        Assert.InRange(lineUp, 0, takeoff - 1);
        Assert.Contains(t, l => l.Contains("PHASE InitialClimb -> Departure"));
    }

    [Fact]
    public void WrongClearanceReadback_IsCorrectedItemByItem()
    {
        var t = RunScenario("""
            00:10 RequestClearance
            00:20 Readback squawk=1234
            00:28 Readback
            00:36 Readback
            """, seed: 1, Manual);
        Fixtures.VerifyTranscript("clearance-wrong-squawk", t);
        Assert.Contains(t, l => l.Contains(": Vueling 1234, negative, squawk "));
        Assert.Contains(t, l => l.Contains("readback correct, when ready for push and start contact Testville Ground 121.700"));
    }

    [Fact]
    public void MissingClearanceItem_IsRequested()
    {
        var t = RunScenario("""
            00:10 RequestClearance
            00:20 Readback -initialclimb
            00:28 Readback
            """, seed: 1, Manual);
        Fixtures.VerifyTranscript("clearance-missing-climb", t);
        Assert.Contains(t, l => l.EndsWith("Vueling 1234, read back the initial climb"));
        Assert.Contains(t, l => l.Contains("PHASE Preflight -> Cleared"));
    }

    [Fact]
    public void NoReadback_IsRepeatedOnce_ThenMissed_ThenLateReadbackAccepted()
    {
        var t = RunScenario("""
            00:10 RequestClearance
            00:38 Readback
            """, seed: 1, Manual);
        Fixtures.VerifyTranscript("clearance-no-readback", t);
        Assert.Equal(2, t.Count(l => l.Contains("ATC Testville Delivery: Vueling 1234, cleared to XDST")));
        Assert.Single(t, l => l.Contains("ifr-clearance: missed"));
        Assert.Contains(t, l => l.Contains("PHASE Preflight -> Cleared"));
    }

    [Fact]
    public void TaxiReadbackWithoutHoldShort_IsRequested()
    {
        var planner = new FixedTaxiPlanner(["K", "M"], ["06"]);
        var t = RunScenario("""
            00:10 RequestClearance
            00:20 Readback
            00:26 Readback
            00:45 RequestPushStart
            01:40 RequestTaxi
            01:50 Readback -holdshorts
            01:58 Readback
            """, seed: 1, Manual, new AtcBrainOptions { TaxiPlanner = planner });
        Fixtures.VerifyTranscript("taxi-missing-holdshort", t);
        Assert.Contains(t, l => l.Contains("taxi to holding point A1 runway 36 via K, M, hold short runway 06"));
        Assert.Contains(t, l => l.EndsWith("Vueling 1234, read back the hold short"));
        Assert.Contains(t, l => l.Contains("taxi: correct"));
    }

    [Fact]
    public void WrongHandoffFrequency_IsCorrected()
    {
        var t = RunScenario("""
            00:10 RequestClearance
            00:20 Readback
            00:26 Readback
            00:45 RequestPushStart
            01:40 RequestTaxi
            01:50 Readback
            05:02 Readback frequency=118200
            05:08 Readback
            """, seed: 1, Manual);
        Fixtures.VerifyTranscript("handoff-wrong-frequency", t);
        Assert.Contains(t, l => l.EndsWith("Vueling 1234, negative, contact Testville Tower 118.100"));
        Assert.Contains(t, l => l.Contains("contact-tower: correct"));
    }

    [Fact]
    public void RequestToTheWrongUnit_IsRedirected()
    {
        // Calls Ground for push before the clearance: Ground redirects to Delivery.
        var t = RunScenario("00:45 RequestPushStart", seed: 1, Manual);
        Fixtures.VerifyTranscript("push-before-clearance", t);
        Assert.Contains(t, l => l.EndsWith("Vueling 1234, unable, contact Testville Delivery 121.850"));
    }

    [Fact]
    public void RequestWhileAnExchangeIsOpen_GetsStandBy_ThenIsAnswered()
    {
        var t = RunScenario("""
            00:10 RequestClearance
            00:45 RequestPushStart
            00:46 RequestTaxi
            """, seed: 1);
        Fixtures.VerifyTranscript("standby-then-taxi", t);
        int standBy = t.ToList().FindIndex(l => l.EndsWith("Vueling 1234, stand by"));
        int taxi = t.ToList().FindIndex(l => l.Contains("taxi to holding point"));
        Assert.InRange(standBy, 0, taxi - 1);
        Assert.Contains(t, l => l.Contains("PHASE Pushback -> TaxiOut"));
    }

    [Fact]
    public void CallOnAFrequencyWithNoStation_GoesUnanswered()
    {
        var recording = Build(30).Select(s => s with { Com1ActiveKhz = 130_000 }).ToArray();
        var brain = new AtcBrain();
        var t = ReplayRunner.Run(brain, recording, IntentScript.Parse("00:10 RequestClearance"), Plan, Origin, Destination, 1);
        Assert.Contains(t, l => l.Contains("no station"));
        Assert.DoesNotContain(t, l => l.Contains(" ATC "));
    }

    [Fact]
    public void Mayday_IsAcknowledgedImmediately()
    {
        var t = RunScenario("00:10 Mayday", seed: 1, Manual);
        Assert.Contains(t, l => l == "[00:00:10.000] ATC Testville Delivery: Vueling 1234, roger mayday");
    }

    /// <summary>A taxi planner returning a fixed route, to test route and hold-short readbacks before step 8.</summary>
    private sealed class FixedTaxiPlanner(IReadOnlyList<string> via, IReadOnlyList<string> holdShorts) : ITaxiPlanner
    {
        public TaxiPlan PlanToHoldingPoint(AirportData airport, Core.Geo.GeoPoint from, RunwayEnd runway)
        {
            var fallback = new FallbackTaxiPlanner().PlanToHoldingPoint(airport, from, runway);
            return fallback with { Via = via, HoldShorts = holdShorts };
        }
    }
}
