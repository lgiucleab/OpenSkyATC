namespace OpenSkyAtc.Replay.Tests.Harness;

/// <summary>Locates tests/fixtures and checks transcripts against approved files.</summary>
public static class Fixtures
{
    public static string Root { get; } = FindRoot();

    public static string RecordingsDir => Path.Combine(Root, "recordings");
    public static string TranscriptsDir => Path.Combine(Root, "transcripts");

    /// <summary>
    /// Compares a transcript with transcripts/{name}.approved.txt. On a mismatch or a missing file,
    /// writes {name}.received.txt next to it and fails. To approve, rename received to approved.
    /// </summary>
    public static void VerifyTranscript(string name, IReadOnlyList<string> transcript)
    {
        Directory.CreateDirectory(TranscriptsDir);
        string approved = Path.Combine(TranscriptsDir, $"{name}.approved.txt");
        string received = Path.Combine(TranscriptsDir, $"{name}.received.txt");
        string actual = string.Join("\n", transcript) + "\n";

        if (File.Exists(approved) && File.ReadAllText(approved).ReplaceLineEndings("\n") == actual)
        {
            File.Delete(received);
            return;
        }

        File.WriteAllText(received, actual);
        Assert.Fail(File.Exists(approved)
            ? $"Transcript '{name}' changed. Compare {received} with {approved}; rename received to approved if the change is intended."
            : $"No approved transcript for '{name}'. Review {received} and rename it to {name}.approved.txt.");
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "tests", "fixtures");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("Could not find tests/fixtures above " + AppContext.BaseDirectory);
    }
}
