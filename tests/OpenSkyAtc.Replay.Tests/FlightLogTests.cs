using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Recording;
using OpenSkyAtc.Core.Tests.Planning;
using OpenSkyAtc.Phraseology;
using OpenSkyAtc.Replay.Tests.Harness;

namespace OpenSkyAtc.Replay.Tests;

/// <summary>
/// The app's automatic flight log: a flight recorded as the app records it, replayed through a new brain, gives the
/// same ATC, call for call (what makes a log from the owner's flight useful for fixing what went wrong).
/// </summary>
public class FlightLogTests
{
    private static (List<string> Lines, IReadOnlyList<string> Transcript) FlyRecorded(SimulatedPilotOptions options)
    {
        var lines = new List<string>();
        var pilot = new SimulatedPilot(options with
        {
            Recorder = new FlightRecorder(lines.Add),
            // the synthetic route's airports are not sim dumps: placeholders here, the real ones are passed to the replay
            RecorderStart = (plan, seed) => new FlightLogStart(plan, SyntheticAirports.Grid(), SyntheticAirports.Grid(), SyntheticRoute.Centers, seed, 10, DateTime.UnixEpoch, "test"),
        });
        var transcript = pilot.Fly(seed: 3);
        Assert.True(pilot.Brain.Session.Ended);
        return (lines, transcript);
    }

    private static IReadOnlyList<string> Replayed(IEnumerable<string> lines)
    {
        var log = FlightLogFormat.Parse(lines);
        var brain = FlightLogReplayer.Replay(log, airports: (SyntheticRoute.Origin, SyntheticRoute.Destination));
        return ReplayRunner.Transcript(brain.Session, new IcaoRenderer(log.Start.Seed));
    }

    [Fact]
    public void ARecordedFlight_ReplaysToTheSameAtc()
    {
        var (lines, transcript) = FlyRecorded(new SimulatedPilotOptions());
        Assert.Equal(transcript, Replayed(lines));
    }

    [Fact]
    public void AFlightWithMistakes_ReplaysToTheSameAtc()
    {
        var (lines, transcript) = FlyRecorded(new SimulatedPilotOptions
        {
            WrongSquawkReadback = true,
            LateFrequencyReadback = TimeSpan.FromSeconds(30),
            LateAirborneHandoff = TimeSpan.FromSeconds(70),
            AltitudeOvershootFt = 700,
        });
        Assert.Contains(lines, l => l.Contains("\"k\":\"intent\"") && l.Contains("\"squawk\":\"1234\""));
        Assert.Equal(transcript, Replayed(lines));
    }

    [Fact]
    public void Lines_HaveTheirKinds_AndACutOffLastLineIsIgnored()
    {
        var (lines, _) = FlyRecorded(new SimulatedPilotOptions());
        Assert.StartsWith("{\"k\":\"start\",\"format\":\"openskyatc-flightlog\"", lines[0]);
        Assert.Contains(lines, l => l.StartsWith("{\"k\":\"state\""));
        Assert.Contains(lines, l => l.StartsWith("{\"k\":\"intent\""));
        var log = FlightLogFormat.Parse([.. lines, "{\"k\":\"state\",\"lati"]);
        Assert.Equal(lines.Count(l => l.StartsWith("{\"k\":\"state\"")), log.States.Count);
        Assert.Contains(log.Events.OfType<LoggedIntent>(), i => i.Intent is RequestClearance);
    }

    [Fact]
    public void NotAFlightLog_IsRefused()
    {
        Assert.Throws<FormatException>(() => FlightLogFormat.Parse(["{\"k\":\"state\"}"]));
        Assert.Throws<FormatException>(() => FlightLogFormat.Parse(["{\"k\":\"start\",\"format\":\"other\"}"]));
    }
}
