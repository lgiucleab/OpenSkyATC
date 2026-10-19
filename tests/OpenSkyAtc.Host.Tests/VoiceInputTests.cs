using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Host;
using OpenSkyAtc.Voice.Input;

namespace OpenSkyAtc.Host.Tests;

public class KeyboardPushToTalkTests
{
    [Theory]
    [InlineData("F12", new[] { 0x7B })]
    [InlineData("ctrl+space", new[] { 0xA2, 0x20 })]
    [InlineData("RCtrl", new[] { 0xA3 })]
    [InlineData("NumPad0", new[] { 0x60 })]
    [InlineData("Shift+T", new[] { 0xA0, 'T' })]
    public void Parses(string spec, int[] firstCodes) =>
        Assert.Equal(firstCodes, KeyboardPushToTalk.Parse(spec)!.Select(k => k[0]));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Hyper+Q")]
    public void UnknownOrEmpty_IsNull(string? spec) => Assert.Null(KeyboardPushToTalk.Parse(spec));

    [Fact]
    public void HeldOnlyWithEveryKeyDown_EitherCtrl()
    {
        var keys = KeyboardPushToTalk.Parse("Ctrl+Space")!;
        Assert.False(KeyboardPushToTalk.IsHeld(keys, new HashSet<int> { 0x20 }));
        Assert.True(KeyboardPushToTalk.IsHeld(keys, new HashSet<int> { 0xA3, 0x20 }));
        Assert.True(KeyboardPushToTalk.IsHeld(keys, new HashSet<int> { 0xA2, 0x20, 0x41 }));
    }
}

public class VoiceInputTests
{
    private static readonly ParseContext Ctx = new([new RequestTaxi()]);

    [Fact]
    public void AClearCall_IsActedOn() =>
        Assert.IsType<RequestTaxi>(VoiceInput.Understand("Iberia 1234 request taxi", 0.9, Ctx, 0.4));

    [Fact]
    public void AnUnsureCall_GetsSayAgain() =>
        Assert.Equal(new Unreadable("Iberia 1234 request taxi"), VoiceInput.Understand("Iberia 1234 request taxi", 0.3, Ctx, 0.4));

    [Fact]
    public void ACallWithNoIntent_GetsSayAgain() =>
        Assert.IsType<Unreadable>(VoiceInput.Understand("lovely weather today", 0.95, Ctx, 0.4));
}

public class HostErrorsTests
{
    [Fact]
    public void AnErrorKind_IsReportedOnce()
    {
        string folder = Path.Combine(Path.GetTempPath(), "openskyatc-errors-" + Guid.NewGuid().ToString("N"));
        string real = HostErrors.Folder;
        HostErrors.Folder = folder; // never the user's real log folder
        try
        {
            string where = "test-" + Guid.NewGuid().ToString("N");
            var error = new InvalidOperationException("boom");
            Assert.True(HostErrors.Record(where, error));
            Assert.False(HostErrors.Record(where, error));
            Assert.True(HostErrors.Record(where, new ArgumentException("other kind")));
            Assert.Single(Directory.GetFiles(folder, "errors-*.txt"));
        }
        finally
        {
            HostErrors.Folder = real;
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
