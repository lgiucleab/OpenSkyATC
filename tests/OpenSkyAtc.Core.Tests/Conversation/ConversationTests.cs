using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Messages;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Radio;
using OpenSkyAtc.Core.Random;

namespace OpenSkyAtc.Core.Tests.Conversation;

public class ReadbackVerifierTests
{
    private static readonly KeyValuePair<ReadbackItem, string>[] Clearance =
    [
        new(ReadbackItem.Destination, "LEMD"),
        new(ReadbackItem.InitialClimb, "5000FT"),
        new(ReadbackItem.Squawk, "4521"),
    ];

    [Fact]
    public void AllItemsMatching_IsCorrect_IgnoringCaseAndSpaces()
    {
        var verdict = ReadbackVerifier.Check(Clearance, new Dictionary<ReadbackItem, string>
        {
            [ReadbackItem.Destination] = " lemd ",
            [ReadbackItem.InitialClimb] = "5000ft",
            [ReadbackItem.Squawk] = "4521",
            [ReadbackItem.Qnh] = "1013", // extra items are ignored
        });
        Assert.True(verdict.IsCorrect);
    }

    [Fact]
    public void ReportsWrongAndMissingSeparately()
    {
        var verdict = ReadbackVerifier.Check(Clearance, new Dictionary<ReadbackItem, string>
        {
            [ReadbackItem.Destination] = "LEMD",
            [ReadbackItem.Squawk] = "4512",
        });
        Assert.Equal([ReadbackItem.Squawk], verdict.Wrong);
        Assert.Equal([ReadbackItem.InitialClimb], verdict.Missing);
    }

    [Fact]
    public void BlankValue_CountsAsMissing()
    {
        var verdict = ReadbackVerifier.Check(Clearance[..1], new Dictionary<ReadbackItem, string> { [ReadbackItem.Destination] = " " });
        Assert.Equal([ReadbackItem.Destination], verdict.Missing);
    }

    [Theory]
    [InlineData("02,13", "13,02", true)]
    [InlineData("02,13", "02", false)]
    [InlineData("02", "02, 13", false)]
    public void HoldShorts_CompareAsASet(string expected, string heard, bool correct)
    {
        var verdict = ReadbackVerifier.Check([new(ReadbackItem.HoldShorts, expected)], new Dictionary<ReadbackItem, string> { [ReadbackItem.HoldShorts] = heard });
        Assert.Equal(correct, verdict.IsCorrect);
    }

    [Fact]
    public void CanonicalValues()
    {
        Assert.Equal("FL070", ReadbackValues.Altitude(AltitudeTarget.FlightLevel(70)));
        Assert.Equal("5000FT", ReadbackValues.Altitude(AltitudeTarget.Feet(5000)));
        Assert.Equal("0443", ReadbackValues.Squawk(443));
        Assert.Equal("FLIGHT PLANNED ROUTE", ReadbackValues.Route(null));
        Assert.Equal("121850", ReadbackValues.Frequency(121_850));
    }
}

public class ConversationManagerTests
{
    private static readonly ControllerUnit Delivery = new(ControllerRole.Delivery, "Testville Delivery", 121_850);
    private static readonly ControllerUnit Ground = new(ControllerRole.Ground, "Testville Ground", 121_700);

    private readonly TransmissionQueue _queue = new();
    private readonly EventLog _log = new();
    private readonly ConversationManager _conv;

    public ConversationManagerTests() => _conv = new ConversationManager(_queue, new SeededRandom(1), _log);

    private static ReadbackExpectation SquawkExpectation() =>
        ReadbackExpectation.Builder().Add(ReadbackItem.Squawk, "4521", new Squawk(4521)).Build();

    /// <summary>Sends everything due, as the brain does.</summary>
    private IReadOnlyList<QueuedTransmission> Send(double seconds)
    {
        var due = _queue.TakeDue(TimeSpan.FromSeconds(seconds));
        foreach (var t in due) _conv.OnSent(t, TimeSpan.FromSeconds(seconds));
        return due;
    }

    [Fact]
    public void ExchangeStates_FollowSendAndReadback()
    {
        var ex = _conv.Transmit(Delivery, [new Squawk(4521)], TransmissionPriority.Reply, TimeSpan.FromSeconds(2), SquawkExpectation());
        Assert.Equal(ExchangeState.Queued, ex.State);
        Assert.True(_conv.IsBusy(Delivery));
        Assert.False(_conv.IsBusy(Ground));

        Send(2);
        Assert.Equal(ExchangeState.AwaitingReadback, ex.State);
        Assert.Same(ex, _conv.PendingReadback());

        Assert.True(_conv.HandleReadback(Delivery, new Dictionary<ReadbackItem, string> { [ReadbackItem.Squawk] = "4521" }, null, TimeSpan.FromSeconds(5), TimeSpan.Zero));
        Assert.Equal(ExchangeState.Correct, ex.State);
        Assert.False(_conv.IsBusy(Delivery));
    }

    [Fact]
    public void NoReadbackRequired_IsDoneWhenSent()
    {
        var ex = _conv.Transmit(Ground, [new PushStartApproved()], TransmissionPriority.Reply, TimeSpan.Zero);
        Send(0);
        Assert.Equal(ExchangeState.Done, ex.State);
    }

    [Fact]
    public void WrongReadback_QueuesNegativeAndCorrection_AndNarrowsTheItems()
    {
        var expectation = ReadbackExpectation.Builder()
            .Add(ReadbackItem.InitialClimb, "5000FT", new ClimbTo(AltitudeTarget.Feet(5000)))
            .Add(ReadbackItem.Squawk, "4521", new Squawk(4521))
            .Build();
        var ex = _conv.Transmit(Delivery, [new Roger()], TransmissionPriority.Reply, TimeSpan.Zero, expectation);
        Send(0);

        _conv.HandleReadback(Delivery, new Dictionary<ReadbackItem, string> { [ReadbackItem.InitialClimb] = "5000FT", [ReadbackItem.Squawk] = "4512" }, null, TimeSpan.FromSeconds(5), TimeSpan.Zero);

        var correction = Assert.Single(Send(10));
        Assert.Equal<AtcMessagePart>([new Negative(), new Squawk(4521)], correction.Parts);
        Assert.Equal([ReadbackItem.Squawk], ex.Outstanding.Select(kv => kv.Key));
        Assert.Equal(1, ex.Corrections);
    }

    [Fact]
    public void MissingItem_AsksForIt()
    {
        _conv.Transmit(Delivery, [new Squawk(4521)], TransmissionPriority.Reply, TimeSpan.Zero, SquawkExpectation());
        Send(0);
        _conv.HandleReadback(Delivery, new Dictionary<ReadbackItem, string>(), null, TimeSpan.FromSeconds(5), TimeSpan.Zero);

        var request = Assert.Single(Send(10));
        Assert.Equal([ReadbackItem.Squawk], Assert.IsType<ReadBackRequest>(Assert.Single(request.Parts)).Items);
    }

    [Fact]
    public void NoReadback_RepeatsOnceAfterTimeout_ThenMarksMissed()
    {
        var ex = _conv.Transmit(Delivery, [new Squawk(4521)], TransmissionPriority.Reply, TimeSpan.Zero, SquawkExpectation());
        Send(0);

        _conv.Tick(TimeSpan.FromSeconds(9.75), TimeSpan.Zero);
        Assert.Empty(_queue.Pending);

        _conv.Tick(TimeSpan.FromSeconds(10), TimeSpan.Zero);
        Assert.Equal(TransmissionPriority.Instruction, Assert.Single(Send(10)).Priority);

        _conv.Tick(TimeSpan.FromSeconds(20), TimeSpan.Zero);
        Assert.Equal(ExchangeState.Missed, ex.State);
        Assert.Single(_log.Entries.OfType<ReadbackMissed>());

        _conv.Tick(TimeSpan.FromSeconds(60), TimeSpan.Zero);
        Assert.Empty(_queue.Pending); // never loops
        Assert.Single(_log.Entries.OfType<ReadbackMissed>());
    }

    [Fact]
    public void ReadbackOnAnotherUnit_IsNotMatched()
    {
        _conv.Transmit(Delivery, [new Squawk(4521)], TransmissionPriority.Reply, TimeSpan.Zero, SquawkExpectation());
        Send(0);
        Assert.False(_conv.HandleReadback(Ground, new Dictionary<ReadbackItem, string> { [ReadbackItem.Squawk] = "4521" }, null, TimeSpan.FromSeconds(3), TimeSpan.Zero));
    }

    [Fact]
    public void SayAgain_RepeatsTheLastTransmission()
    {
        _conv.Transmit(Ground, [new PushStartApproved()], TransmissionPriority.Reply, TimeSpan.Zero);
        Send(0);
        Assert.True(_conv.SayAgain(Ground, TimeSpan.FromSeconds(5)));
        Assert.Equal<AtcMessagePart>([new PushStartApproved()], Assert.Single(Send(10)).Parts);
        Assert.False(_conv.SayAgain(Delivery, TimeSpan.FromSeconds(5)));
    }
}

public class TransmissionQueueTests
{
    private static readonly ControllerUnit Tower = new(ControllerRole.Tower, "Testville Tower", 118_100);

    [Fact]
    public void TakeDue_OrdersByPriorityThenDueThenQueueOrder()
    {
        var q = new TransmissionQueue();
        q.Enqueue(Tower, [new Roger()], TransmissionPriority.Info, TimeSpan.FromSeconds(1));
        q.Enqueue(Tower, [new StandBy()], TransmissionPriority.Instruction, TimeSpan.FromSeconds(2));
        q.Enqueue(Tower, [new Negative()], TransmissionPriority.Instruction, TimeSpan.FromSeconds(1));
        q.Enqueue(Tower, [new SayAgainRequest()], TransmissionPriority.Urgent, TimeSpan.FromSeconds(3));
        q.Enqueue(Tower, [new ReadbackCorrect()], TransmissionPriority.Reply, TimeSpan.FromSeconds(9));

        var due = q.TakeDue(TimeSpan.FromSeconds(5));

        Assert.Equal<AtcMessagePart>([new SayAgainRequest(), new Negative(), new StandBy(), new Roger()], due.Select(d => d.Parts[0]));
        Assert.Single(q.Pending);
    }
}
