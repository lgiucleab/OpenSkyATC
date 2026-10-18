using OpenSkyAtc.Voice.Input;

namespace OpenSkyAtc.Voice.Tests;

public class MicrophoneTests
{
    [Fact]
    public void AMissingDevice_IsReported_NotThrown()
    {
        using var microphone = new Microphone(device: 99);
        Assert.False(microphone.Start(out string? error));
        Assert.False(microphone.IsRecording);
        Assert.False(string.IsNullOrEmpty(error));
    }
}
