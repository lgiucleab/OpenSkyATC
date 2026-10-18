using OpenSkyAtc.SimConnect;

namespace OpenSkyAtc.Host.Tests;

public class SimConnectLibraryTests
{
    [Fact]
    public void TheAppFolder_IsSearchedFirst_ThenTheSdk() =>
        Assert.Equal(AppContext.BaseDirectory, SimConnectLibrary.Candidates().First());

    [Fact]
    public void BothDllsInOneFolder_AreFound_EitherAloneIsNot()
    {
        string folder = Path.Combine(Path.GetTempPath(), "openskyatc-simconnect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, SimConnectLibrary.ManagedFileName), "");
            Assert.Null(SimConnectLibrary.Locate(folder));
            File.WriteAllText(Path.Combine(folder, SimConnectLibrary.NativeFileName), "");
            var found = SimConnectLibrary.Locate(folder);
            Assert.NotNull(found);
            Assert.Equal(folder, Path.GetDirectoryName(found.Value.Managed));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
