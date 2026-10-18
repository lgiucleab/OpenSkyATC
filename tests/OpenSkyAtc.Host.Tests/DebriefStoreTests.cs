using OpenSkyAtc.Core.Debrief;
using OpenSkyAtc.Host;

namespace OpenSkyAtc.Host.Tests;

public class DebriefStoreTests
{
    [Fact]
    public void Saves_AndListsNewestFirst_SkippingUnreadableFiles()
    {
        string folder = Path.Combine(Path.GetTempPath(), "openskyatc-debrief-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DebriefStore(folder);
            var older = new FlightDebrief { Callsign = "IBE1234", Origin = "LEMD", Destination = "LEBL", Score = 82, EndedAt = new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.Zero) };
            var newer = older with { Score = 95, EndedAt = older.EndedAt!.Value.AddHours(3) };
            string path = store.Save(older);
            store.Save(newer);
            File.WriteAllText(Path.Combine(folder, "broken.json"), "{not json");

            Assert.EndsWith("LEMD-LEBL IBE1234.json", path);
            var list = store.List();
            Assert.Equal([95, 82], list.Select(s => s.Debrief.Score));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void NoFolderYet_IsAnEmptyList() =>
        Assert.Empty(new DebriefStore(Path.Combine(Path.GetTempPath(), "openskyatc-none-" + Guid.NewGuid().ToString("N"))).List());
}
