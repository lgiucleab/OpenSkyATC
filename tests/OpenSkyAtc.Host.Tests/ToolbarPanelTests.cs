using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Host;

namespace OpenSkyAtc.Host.Tests;

public class ToolbarPanelTests
{
    [Fact]
    public void State_IsCamelCaseJson_WithoutNulls()
    {
        string json = PanelProtocol.Serialize(new PanelState
        {
            Flight = true, Frequency = "121.630",
            Buttons = [new PanelButton(0, PanelProtocol.Key(0, new RequestTaxi()), "Request taxi", true)],
            Calls = [new PanelCall("Barajas Ground", "Iberia 1234, taxi...")],
        });
        Assert.Contains("\"version\":1", json);
        Assert.Contains("\"frequency\":\"121.630\"", json);
        Assert.Contains("\"key\":\"0:RequestTaxi\"", json);
        Assert.Contains("\"station\":\"Barajas Ground\"", json);
        Assert.DoesNotContain("\"hint\"", json);
        Assert.DoesNotContain("\"voiceReadback\"", json);
    }

    [Theory]
    [InlineData("{\"cmd\":\"hello\"}")]
    [InlineData("{\"cmd\":\"intent\",\"id\":2,\"key\":\"2:RequestTaxi\"}")]
    [InlineData("{\"cmd\":\"arrows\",\"on\":false}")]
    public void Commands_Parse(string text) => Assert.NotNull(PanelProtocol.Parse(text));

    [Fact]
    public void Commands_HaveTheirValues()
    {
        Assert.Equal(new PanelCommand.Intent(2, "2:RequestTaxi"), PanelProtocol.Parse("{\"cmd\":\"intent\",\"id\":2,\"key\":\"2:RequestTaxi\"}"));
        Assert.Equal(new PanelCommand.Arrows(false), PanelProtocol.Parse("{\"cmd\":\"arrows\",\"on\":false}"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"cmd\":\"launch\"}")]
    [InlineData("{\"cmd\":\"intent\",\"id\":\"x\"}")]
    [InlineData("{\"cmd\":\"arrows\",\"on\":\"yes\"}")]
    [InlineData("{\"cmd\":\"intent\",\"id\":1,\"key\":7}")]
    public void Malformed_IsNull(string text) => Assert.Null(PanelProtocol.Parse(text));

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Link_SendsChanges_AtMostTwiceASecond_AndAHeartbeat()
    {
        var link = new PanelLink();
        Assert.True(link.ShouldSend("a", S(0), paused: false));     // first state
        Assert.False(link.ShouldSend("b", S(0.2), paused: false));  // too soon
        Assert.True(link.ShouldSend("b", S(0.6), paused: false));   // change
        Assert.False(link.ShouldSend("b", S(1.5), paused: false));  // nothing new
        Assert.True(link.ShouldSend("b", S(2.7), paused: false));   // heartbeat
    }

    [Fact]
    public void Link_WhilePaused_SendsEvery5sAtMost()
    {
        var link = new PanelLink();
        Assert.True(link.ShouldSend("a", S(0), paused: true));
        Assert.False(link.ShouldSend("b", S(1), paused: true));
        Assert.False(link.ShouldSend("c", S(4.9), paused: true));
        Assert.True(link.ShouldSend("c", S(5), paused: true));
    }

    [Fact]
    public void Link_Reset_SendsAtOnce()
    {
        var link = new PanelLink();
        link.ShouldSend("a", S(0), paused: false);
        link.Reset();
        Assert.True(link.ShouldSend("a", S(0.1), paused: false));
    }
}
