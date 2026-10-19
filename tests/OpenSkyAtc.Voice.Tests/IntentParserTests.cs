using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Voice.Input;

namespace OpenSkyAtc.Voice.Tests;

public class SpokenTextTests
{
    [Theory]
    [InlineData("One two one decimal six three five", "121.635")]
    [InlineData("contact 121.63", "CONTACT 121.63")]
    [InlineData("Flight level one two zero", "FLIGHT LEVEL 120")]
    [InlineData("FL120", "FL 120")]
    [InlineData("climb 5,000 feet", "CLIMB 5000 FEET")]
    [InlineData("three thousand five hundred feet", "3500 FEET")]
    [InlineData("one zero thousand feet", "10000 FEET")]
    [InlineData("runway 36L", "RUNWAY 36 L")]
    [InlineData("runway three six left", "RUNWAY 36 LEFT")]
    [InlineData("direct tango oscar bravo echo kilo", "DIRECT TOBEK")]
    [InlineData("squawk four seven two one", "SQUAWK 4721")]
    [InlineData("heading two niner zero", "HEADING 290")]
    [InlineData("Iberia one two three four.", "IBERIA 1234")]
    [InlineData("speed two hundred knots", "SPEED 200 KNOTS")]
    [InlineData("twenty five", "25")]
    [InlineData("X-ray", "X")]
    public void Normalizes(string transcript, string expected) => Assert.Equal(expected, SpokenText.Normalize(transcript));

    [Fact]
    public void AVeryLongNumber_BeforeThousand_DoesNotOverflow()
    {
        string said = string.Join(" ", Enumerable.Repeat("nine", 25)) + " thousand feet";
        Assert.StartsWith(new string('9', 25), SpokenText.Normalize(said));
        Assert.StartsWith("9999999999999999999 HUNDRED", SpokenText.Normalize("9999999999999999999 hundred"));
    }
}

public class IntentParserTests
{
    private static ParseContext Ctx(params PilotIntent[] available) => new(available, new Dictionary<string, string> { ["Barcelona"] = "LEBL" });

    private static Readback Pending(params (ReadbackItem Item, string Value)[] items) =>
        new(items.ToDictionary(i => i.Item, i => i.Value), ExchangeId: 7);

    private static PilotIntent? Parse(string said, params PilotIntent[] available) => IntentParser.Parse(said, Ctx(available)).Intent;

    // ---- requests and reports ----

    [Fact]
    public void ClearanceRequest_WithAtisLetter()
    {
        var intent = Parse("Madrid Delivery, Iberia one two three four, request IFR clearance to Barcelona, information Bravo", new RequestClearance());
        Assert.Equal(new RequestClearance("B"), intent);
    }

    [Theory]
    [InlineData("Madrid Ground, Iberia 1234, stand 12, request push and start", typeof(RequestPushStart))]
    [InlineData("Iberia 1234 ready for pushback", typeof(RequestPushStart))]
    [InlineData("Iberia 1234 request taxi", typeof(RequestTaxi))]
    [InlineData("ready to taxi, Iberia 1234", typeof(RequestTaxi))]
    [InlineData("Iberia 1234 holding point A1, ready for departure", typeof(ReadyForDeparture))]
    [InlineData("stopping, Iberia 1234", typeof(RejectTakeoff))]
    [InlineData("request descent, Iberia 1234", typeof(RequestDescent))]
    [InlineData("established ILS runway 25 right", typeof(ReportEstablished))]
    [InlineData("Iberia 1234 going around", typeof(GoingAround))]
    [InlineData("runway vacated, Iberia 1234", typeof(ReportVacated))]
    [InlineData("Mayday mayday mayday, Iberia 1234, engine fire", typeof(Mayday))]
    [InlineData("pan pan, pan pan, pan pan", typeof(PanPan))]
    [InlineData("say again", typeof(SayAgain))]
    [InlineData("wilco, Iberia 1234", typeof(Wilco))]
    public void Requests(string said, Type expected) => Assert.IsType(expected, Parse(said));

    [Fact]
    public void LevelRequest() =>
        Assert.Equal(new RequestLevel(AltitudeTarget.FlightLevel(360)), Parse("Iberia 1234 request flight level three six zero"));

    [Fact]
    public void DirectRequest() =>
        Assert.Equal(new RequestDirect("TOBEK"), Parse("request direct tango oscar bravo echo kilo"));

    [Fact]
    public void CheckIn_PassingAndCleared() =>
        Assert.Equal(new CheckIn(8000, AltitudeTarget.FlightLevel(150)),
            Parse("Madrid Control, Iberia 1234, passing 8000 feet climbing flight level 150"));

    [Fact]
    public void Unable_KeepsTheReason() =>
        Assert.Equal(new Unable("due weather"), Parse("unable due weather"));

    [Theory]
    [InlineData("the weather is lovely today")]
    [InlineData("")]
    [InlineData("um")]
    public void Nonsense_GivesNoIntent(string said) => Assert.Null(Parse(said));

    // ---- readbacks ----

    private static readonly Readback ClearanceDelivery = Pending(
        (ReadbackItem.Destination, "LEBL"), (ReadbackItem.Route, "PINA4N"), (ReadbackItem.InitialClimb, "6000FT"), (ReadbackItem.Squawk, "4721"));

    [Theory]
    [InlineData("Cleared to LEBL, PINA4N departure, climb 6000 feet, squawk 4721, Iberia 1234")]
    [InlineData("cleared to Barcelona, Pina four November departure, climb six thousand feet, squawk four seven two one, Iberia one two three four")]
    public void ClearanceReadback_Correct(string said)
    {
        var r = Assert.IsType<Readback>(Parse(said, ClearanceDelivery));
        Assert.Equal(7, r.ExchangeId);
        Assert.True(ReadbackVerifier.Check(ClearanceDelivery.Items.ToList(), r.Items).IsCorrect);
    }

    [Fact]
    public void ClearanceReadback_WrongSquawk_IsKeptAsHeard()
    {
        var r = Assert.IsType<Readback>(Parse("cleared to Barcelona, PINA 4N departure, 6000 feet, squawk 4712", ClearanceDelivery));
        var verdict = ReadbackVerifier.Check(ClearanceDelivery.Items.ToList(), r.Items);
        Assert.Equal([ReadbackItem.Squawk], verdict.Wrong);
        Assert.Empty(verdict.Missing);
    }

    [Fact]
    public void ClearanceReadback_MissingItem()
    {
        var r = Assert.IsType<Readback>(Parse("cleared to Barcelona, PINA 4N departure, squawk 4721", ClearanceDelivery));
        var verdict = ReadbackVerifier.Check(ClearanceDelivery.Items.ToList(), r.Items);
        Assert.Equal([ReadbackItem.InitialClimb], verdict.Missing);
    }

    [Fact]
    public void TaxiReadback_RunwayAndHoldShortsApart()
    {
        var taxi = Pending((ReadbackItem.HoldingPoint, "A1"), (ReadbackItem.Runway, "36L"), (ReadbackItem.HoldShorts, "36R"));
        var r = Assert.IsType<Readback>(Parse("taxi holding point alpha one runway three six left, hold short runway three six right, Iberia 1234", taxi));
        Assert.True(ReadbackVerifier.Check(taxi.Items.ToList(), r.Items).IsCorrect);
    }

    [Theory]
    [InlineData("push and start approved, facing south, Iberia 1234", ReadbackItem.Facing, "SOUTH")]
    [InlineData("pushing back facing south east", ReadbackItem.Facing, "SOUTHEAST")]
    public void PushReadback(string said, ReadbackItem item, string value)
    {
        var push = Pending((ReadbackItem.Clearance, "PUSH"), (ReadbackItem.Facing, "SOUTH"));
        var r = Assert.IsType<Readback>(Parse(said, push));
        Assert.Equal("PUSH", r.Items[ReadbackItem.Clearance]);
        Assert.Equal(value, r.Items[item]);
    }

    [Theory]
    [InlineData("lining up and wait runway 36 left", "LINEUP", "36L")]
    [InlineData("cleared for takeoff runway three six left, Iberia 1234", "TAKEOFF", "36L")]
    [InlineData("runway 32 right cleared to land", "LAND", "32R")]
    public void RunwayClearances(string said, string clearance, string runway)
    {
        var expected = Pending((ReadbackItem.Clearance, clearance), (ReadbackItem.Runway, runway));
        var r = Assert.IsType<Readback>(Parse(said, expected));
        Assert.True(ReadbackVerifier.Check(expected.Items.ToList(), r.Items).IsCorrect);
    }

    [Theory]
    [InlineData("contact Madrid departure 121.63, Iberia 1234", ReadbackItem.Frequency, "121630")]
    [InlineData("one two one decimal six three", ReadbackItem.Frequency, "121630")]
    [InlineData("climbing flight level 120", ReadbackItem.Altitude, "FL120")]
    [InlineData("climb and maintain 5000 feet", ReadbackItem.Altitude, "5000FT")]
    [InlineData("turn left heading 270", ReadbackItem.Heading, "270")]
    [InlineData("reduce speed 180 knots", ReadbackItem.Speed, "180")]
    [InlineData("speed two hundred", ReadbackItem.Speed, "200")]
    [InlineData("QNH 1013", ReadbackItem.Qnh, "1013")]
    [InlineData("direct TOBEK", ReadbackItem.DirectTo, "TOBEK")]
    [InlineData("stand 12", ReadbackItem.Stand, "12")]
    public void SingleItems(string said, ReadbackItem item, string value)
    {
        var r = Assert.IsType<Readback>(Parse(said, Pending((item, value))));
        Assert.Equal(value, r.Items[item]);
    }

    [Fact]
    public void WrongAltitude_IsKeptAsHeard()
    {
        var r = Assert.IsType<Readback>(Parse("climbing flight level 130", Pending((ReadbackItem.Altitude, "FL120"))));
        Assert.Equal("FL130", r.Items[ReadbackItem.Altitude]);
    }

    [Fact]
    public void FlightPlannedRoute()
    {
        var expected = Pending((ReadbackItem.Route, ReadbackValues.FlightPlannedRoute));
        var r = Assert.IsType<Readback>(Parse("cleared flight planned route", expected));
        Assert.Equal(ReadbackValues.FlightPlannedRoute, r.Items[ReadbackItem.Route]);
    }

    [Fact]
    public void AnAvailableRequest_WinsOverAPendingReadback()
    {
        var pending = Pending((ReadbackItem.Runway, "36L"));
        Assert.IsType<RequestTaxi>(Parse("request taxi", pending, new RequestTaxi()));
    }

    [Fact]
    public void ClimbingWithAReadbackPending_IsTheReadback_NotACheckIn()
    {
        var pending = Pending((ReadbackItem.Altitude, "FL120"));
        Assert.IsType<Readback>(Parse("climbing flight level 120, Iberia 1234", pending, new CheckIn()));
    }

    [Fact]
    public void WilcoWithAReadbackPending_IsWilco()
    {
        Assert.IsType<Wilco>(Parse("wilco", Pending((ReadbackItem.Squawk, "4721"))));
    }
}
