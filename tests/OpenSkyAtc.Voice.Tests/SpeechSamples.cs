using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Replay.Tests.Harness;
using OpenSkyAtc.Voice.Input;

namespace OpenSkyAtc.Voice.Tests;

/// <summary>
/// What each call in tests/fixtures/speech/phrases.txt must parse to, and the moment it is said in (what ATC is
/// waiting for). A readback sample passes when the verifier finds it correct; a request when the intent matches.
/// </summary>
public static class SpeechSamples
{
    public sealed record Sample(string Id, IReadOnlyList<PilotIntent> Available, PilotIntent? Expected, Readback? Pending)
    {
        public ParseContext Context => new(Pending is null ? Available : [Pending, .. Available],
            new Dictionary<string, string> { ["Barcelona"] = "LEBL", ["Madrid"] = "LEMD" });

        /// <summary>Null when the call parsed as expected, else what went wrong.</summary>
        public string? Check(PilotIntent? intent)
        {
            if (Pending is not null)
            {
                if (intent is not Readback r) return $"expected a readback, got {Describe(intent)}";
                var verdict = ReadbackVerifier.Check(Pending.Items.ToList(), r.Items);
                return verdict.IsCorrect ? null
                    : $"readback: wrong [{string.Join(", ", verdict.Wrong.Select(w => $"{w}={r.Items[w]}"))}] missing [{string.Join(", ", verdict.Missing)}]";
            }
            return Equals(intent, Expected) ? null : $"expected {Describe(Expected)}, got {Describe(intent)}";
        }

        private static string Describe(PilotIntent? i) => i?.ToString() ?? "nothing";
    }

    public static string Dir => Path.Combine(Fixtures.Root, "speech");

    public static IReadOnlyList<SpeechPhrase> Phrases { get; } = SpeechPhrases.Load(Path.Combine(Dir, "phrases.txt"));

    private static Sample Request(string id, PilotIntent expected) => new(id, [expected with { }], expected, null);

    private static Sample Read(string id, params (ReadbackItem Item, string Value)[] items) =>
        new(id, [], null, new Readback(items.ToDictionary(i => i.Item, i => i.Value), ExchangeId: 1));

    public static IReadOnlyDictionary<string, Sample> ById { get; } = new Sample[]
    {
        Request("clearance-request", new RequestClearance("B")),
        Read("clearance-readback", (ReadbackItem.Destination, "LEBL"), (ReadbackItem.Route, "PINA4N"), (ReadbackItem.InitialClimb, "6000FT"), (ReadbackItem.Squawk, "4721")),
        Read("clearance-readback-fpr", (ReadbackItem.Destination, "LEBL"), (ReadbackItem.Route, ReadbackValues.FlightPlannedRoute), (ReadbackItem.InitialClimb, "FL130"), (ReadbackItem.Squawk, "2351")),
        Request("push-request", new RequestPushStart()),
        Read("push-readback", (ReadbackItem.Clearance, "PUSH"), (ReadbackItem.Facing, "SOUTH")),
        Request("taxi-request", new RequestTaxi()),
        Read("taxi-readback", (ReadbackItem.HoldingPoint, "A1"), (ReadbackItem.Runway, "36L"), (ReadbackItem.HoldShorts, "36R")),
        Request("ready", new ReadyForDeparture()),
        Read("lineup-readback", (ReadbackItem.Clearance, "LINEUP"), (ReadbackItem.Runway, "36L")),
        Read("takeoff-readback", (ReadbackItem.Clearance, "TAKEOFF"), (ReadbackItem.Runway, "36L")),
        Read("contact-departure", (ReadbackItem.Frequency, "127100")),
        Request("checkin-departure", new CheckIn(4500, AltitudeTarget.Feet(6000))),
        Read("climb-readback", (ReadbackItem.Altitude, "FL120")),
        Read("heading-readback", (ReadbackItem.Heading, "090")),
        Read("direct-readback", (ReadbackItem.DirectTo, "TOBEK")),
        Request("checkin-control", new CheckIn(18000, AltitudeTarget.FlightLevel(250))),
        Request("level-request", new RequestLevel(AltitudeTarget.FlightLevel(360))),
        Request("direct-request", new RequestDirect("TOBEK")),
        Request("descent-request", new RequestDescent()),
        Read("descent-readback", (ReadbackItem.Altitude, "FL100")),
        Read("speed-readback", (ReadbackItem.Speed, "220")),
        Read("qnh-readback", (ReadbackItem.Altitude, "5000FT"), (ReadbackItem.Qnh, "1013")),
        Request("established", new ReportEstablished()),
        Read("tower-readback", (ReadbackItem.Frequency, "118100")),
        Read("land-readback", (ReadbackItem.Clearance, "LAND"), (ReadbackItem.Runway, "24R")),
        Request("go-around", new GoingAround()),
        Request("vacated", new ReportVacated()),
        Read("stand-readback", (ReadbackItem.Stand, "22")),
        Request("say-again", new SayAgain()),
        Request("wilco", new Wilco()),
        Request("mayday", new Mayday()),
    }.ToDictionary(s => s.Id);

    /// <summary>Words to prime Whisper with, as the app does for this flight.</summary>
    public static string Prompt => SpeechPrompt.Build("Iberia 1234", ["Madrid Ground", "Madrid Delivery", "Madrid Departure", "Madrid Control", "Barcelona Tower"],
        ["PINA4N", "TOBEK", "LEMD", "LEBL", "runway 36L", "runway 24R"]);
}
