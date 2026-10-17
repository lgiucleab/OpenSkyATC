using OpenSkyAtc.Voice.Input;
using Xunit.Abstractions;

namespace OpenSkyAtc.Voice.Tests;

/// <summary>
/// Step 14 done-when: the parser on the phrase list (always), and Whisper + parser on the recorded audio of the same
/// phrases: the committed Piper renderings and, where present, the owner's recordings. The audio tests run only where
/// a Whisper model is installed (`Recorder stt install`).
/// </summary>
public class SpeechSampleTests(ITestOutputHelper output)
{
    public static TheoryData<string> Ids => new(SpeechSamples.Phrases.Select(p => p.Id));

    [Fact]
    public void EveryPhrase_HasAnExpectation() =>
        Assert.Equal(SpeechSamples.Phrases.Select(p => p.Id).Order(), SpeechSamples.ById.Keys.Order());

    [Theory]
    [MemberData(nameof(Ids))]
    public void Parser_OnThePhraseText(string id)
    {
        var phrase = SpeechSamples.Phrases.Single(p => p.Id == id);
        var sample = SpeechSamples.ById[id];
        Assert.Null(sample.Check(IntentParser.Parse(phrase.Text, sample.Context).Intent));
    }

    [Fact]
    public Task Whisper_OnPiperRenderings() => RunAudio("piper", minimumPassRate: 0.85);

    [Fact]
    public Task Whisper_OnTheOwnersRecordings() => RunAudio("owner", minimumPassRate: 0.8);

    private async Task RunAudio(string folder, double minimumPassRate)
    {
        string dir = Path.Combine(SpeechSamples.Dir, folder);
        var store = new WhisperModelStore();
        var model = new[] { WhisperModels.Base, WhisperModels.BaseQ5, WhisperModels.Small, WhisperModels.SmallQ5, WhisperModels.Tiny }.FirstOrDefault(store.IsInstalled);
        var files = SpeechSamples.Phrases.Select(p => (p, File: Path.Combine(dir, p.Id + ".wav"))).Where(x => File.Exists(x.File)).ToList();
        if (model is null || files.Count == 0) return; // no model or no recordings on this machine

        using var recognizer = new SpeechRecognizer(store.PathOf(model));
        int passed = 0;
        foreach (var (phrase, file) in files)
        {
            var heard = await recognizer.RecognizeAsync(SpeechRecognizer.LoadWav(file), SpeechSamples.Prompt);
            var sample = SpeechSamples.ById[phrase.Id];
            string? problem = sample.Check(IntentParser.Parse(heard.Text, sample.Context).Intent);
            if (problem is null) passed++;
            output.WriteLine($"{(problem is null ? "ok  " : "FAIL")} {phrase.Id,-24} p={heard.Confidence:0.00} \"{heard.Text}\"{(problem is null ? "" : "  -> " + problem)}");
        }
        double rate = (double)passed / files.Count;
        output.WriteLine($"{model.Id}, {folder}: {passed}/{files.Count} understood");
        Assert.True(rate >= minimumPassRate, $"{model.Id} on {folder}: {passed}/{files.Count} understood");
    }
}
