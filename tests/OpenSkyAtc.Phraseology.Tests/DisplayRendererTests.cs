using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Messages;
using OpenSkyAtc.Core.Model;

namespace OpenSkyAtc.Phraseology.Tests;

/// <summary>Step 3 display text. Step 4 replaces these with snapshot tests for every part and number rule.</summary>
public class DisplayRendererTests
{
    [Theory]
    [InlineData("VLG1234", "Vueling", "Vueling 1234")]
    [InlineData("ECABC", null, "ECABC")]
    public void Callsign_UsesTelephonyWhenKnown(string code, string? telephony, string expected) =>
        Assert.Equal(expected, DisplayRenderer.Callsign(new AircraftCallsign(code, telephony)));

    [Fact]
    public void Message_IsCallsignThenParts()
    {
        var message = new AtcMessage(TimeSpan.Zero, new ControllerUnit(ControllerRole.Tower, "Barcelona Tower", 118_100),
            new AircraftCallsign("VLG1234", "Vueling"), [new Wind(250, 12), new ClearedTakeoff("24L")], Core.Radio.TransmissionPriority.Reply);
        Assert.Equal("Vueling 1234, wind 250 degrees 12 knots, runway 24L, cleared for takeoff", DisplayRenderer.Render(message));
    }

    public static TheoryData<AtcMessagePart, string> Parts => new()
    {
        { new IfrClearance("LEMD", null, AltitudeTarget.FlightLevel(70), 443), "cleared to LEMD via flight planned route, climb FL070, squawk 0443" },
        { new IfrClearance("LEMD", "NAPSI1C", AltitudeTarget.Feet(5000), 4521), "cleared to LEMD via NAPSI1C departure, climb 5000 ft, squawk 4521" },
        { new Contact("Barcelona Ground", 121_700, ContactCondition.WhenReadyForPushAndStart), "when ready for push and start contact Barcelona Ground 121.700" },
        { new Contact("Barcelona Tower", 118_105), "contact Barcelona Tower 118.105" },
        { new TaxiVia(null, "24L", [], []), "taxi to holding point runway 24L" },
        { new TaxiVia("A1", "24L", ["K", "M"], ["02"]), "taxi to holding point A1 runway 24L via K, M, hold short runway 02" },
        { new Wind(0, 0), "wind calm" },
        { new Wind(50, 7), "wind 050 degrees 7 knots" },
        { new ReadBackRequest([ReadbackItem.Squawk, ReadbackItem.InitialClimb]), "read back the squawk and initial climb" },
        { new UnableContact("Barcelona Delivery", 121_850), "unable, contact Barcelona Delivery 121.850" },
        { new EmergencyAcknowledged(EmergencyKind.PanPan), "roger pan pan" },
    };

    [Theory]
    [MemberData(nameof(Parts))]
    public void Part_RendersItsDisplayText(AtcMessagePart part, string expected) =>
        Assert.Equal(expected, DisplayRenderer.Part(part));
}
