using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Voice.Input;

namespace OpenSkyAtc.Voice.Tests;

public class SpeechPromptTests
{
    private static Readback Pending(params (ReadbackItem Item, string Value)[] items) =>
        new(items.ToDictionary(i => i.Item, i => i.Value), 1);

    [Fact]
    public void ACallAnsweringATC_EndsThePromptWithTheExpectedReadback()
    {
        string prompt = SpeechPrompt.ForCall("Madrid Ground, D-KR.", Pending((ReadbackItem.Frequency, "124230")), "D-KR");
        Assert.EndsWith("124.230, D-KR.", prompt);
        Assert.Equal("Madrid Ground, D-KR.", SpeechPrompt.ForCall("Madrid Ground, D-KR.", null, "D-KR"));
    }

    [Theory]
    [InlineData(ReadbackItem.Frequency, "124230")]
    [InlineData(ReadbackItem.Frequency, "118080")]
    [InlineData(ReadbackItem.Altitude, "FL180")]
    [InlineData(ReadbackItem.Altitude, "FL090")]
    [InlineData(ReadbackItem.InitialClimb, "5000FT")]
    [InlineData(ReadbackItem.Squawk, "2100")]
    [InlineData(ReadbackItem.Runway, "36L")]
    [InlineData(ReadbackItem.Heading, "270")]
    [InlineData(ReadbackItem.Speed, "250")]
    [InlineData(ReadbackItem.Qnh, "1013")]
    [InlineData(ReadbackItem.Clearance, "TAKEOFF")]
    [InlineData(ReadbackItem.Clearance, "PUSH")]
    public void TheWrittenReadback_ParsesBackToTheSameValue(ReadbackItem item, string value)
    {
        var pending = Pending((item, value));
        string written = SpeechPrompt.Written(item, value);
        var r = Assert.IsType<Readback>(IntentParser.Parse(written, new ParseContext([pending])).Intent);
        Assert.True(ReadbackVerifier.Check(pending.Items.ToList(), r.Items).IsCorrect, $"\"{written}\" read as {string.Join(", ", r.Items)}");
    }
}
