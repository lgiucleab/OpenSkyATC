namespace OpenSkyAtc.Voice.Tests;

public class VoiceStoreTests
{
    private static string Temp() => Path.Combine(Path.GetTempPath(), "openskyatc-voices-" + Guid.NewGuid().ToString("N"));

    private static void FakeModel(string folder, bool espeak)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "model.onnx"), "");
        File.WriteAllText(Path.Combine(folder, "tokens.txt"), "");
        if (espeak) Directory.CreateDirectory(Path.Combine(folder, "espeak-ng-data"));
    }

    [Fact]
    public void TheInstallersCopy_IsUsedFirst_WithTheSharedEspeakData()
    {
        string user = Temp(), bundled = Temp();
        try
        {
            FakeModel(Path.Combine(bundled, VoiceCatalog.Kokoro.Archive), espeak: false);
            Directory.CreateDirectory(Path.Combine(bundled, "espeak-ng-data"));
            var store = new VoiceStore(user, bundled);
            Assert.True(store.IsInstalled(VoiceCatalog.Kokoro));
            Assert.Equal(Path.Combine(bundled, VoiceCatalog.Kokoro.Archive), store.FolderOf(VoiceCatalog.Kokoro));
            Assert.Equal(Path.Combine(bundled, "espeak-ng-data"), store.DataDirOf(VoiceCatalog.Kokoro));

            var joe = VoiceCatalog.Get("joe");
            Assert.False(store.IsInstalled(joe));                       // not bundled: downloads go to the user folder
            Assert.Equal(Path.Combine(user, joe.Archive), store.FolderOf(joe));
            FakeModel(Path.Combine(user, joe.Archive), espeak: true);
            Assert.True(store.IsInstalled(joe));
            Assert.Equal(Path.Combine(user, joe.Archive, "espeak-ng-data"), store.DataDirOf(joe)); // a download keeps its own
        }
        finally
        {
            foreach (var d in new[] { user, bundled })
                if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
        }
    }

    [Fact]
    public void Kokoro_HasItsEnglishVoices_WithoutTheSantaVoice()
    {
        Assert.Equal(VoiceEngine.Kokoro, VoiceCatalog.Kokoro.Engine);
        Assert.Equal(27, VoiceCatalog.Kokoro.Speakers.Count);
        Assert.DoesNotContain(19, VoiceCatalog.Kokoro.Speakers);
        Assert.All(VoiceCatalog.Kokoro.Speakers, s => Assert.InRange(s, 0, 27));
        Assert.DoesNotContain(VoiceCatalog.Kokoro, VoiceCatalog.Piper);
    }
}
