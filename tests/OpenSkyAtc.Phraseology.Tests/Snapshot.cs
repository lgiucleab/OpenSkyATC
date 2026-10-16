namespace OpenSkyAtc.Phraseology.Tests;

/// <summary>
/// Approval files in tests/fixtures/snapshots. On a mismatch or a missing file the test writes
/// &lt;name&gt;.received.txt (git-ignored) and fails; rename it to .approved.txt to accept.
/// </summary>
internal static class Snapshot
{
    public static void Verify(string name, IEnumerable<string> lines)
    {
        string dir = Path.Combine(FindFixtures(), "snapshots");
        Directory.CreateDirectory(dir);
        string approved = Path.Combine(dir, $"{name}.approved.txt");
        string received = Path.Combine(dir, $"{name}.received.txt");
        string actual = string.Join("\n", lines) + "\n";

        if (File.Exists(approved) && File.ReadAllText(approved).ReplaceLineEndings("\n") == actual)
        {
            File.Delete(received);
            return;
        }
        File.WriteAllText(received, actual);
        Assert.Fail($"Snapshot '{name}' {(File.Exists(approved) ? "changed" : "is new")}. Review {received} and rename it to {name}.approved.txt.");
    }

    /// <summary>Path of a file relative to the repository root (the folder that holds tests/fixtures).</summary>
    public static string RepoFile(params string[] parts) =>
        Path.Combine([Path.GetDirectoryName(Path.GetDirectoryName(FindFixtures()))!, .. parts]);

    private static string FindFixtures()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "tests", "fixtures");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("Could not find tests/fixtures above " + AppContext.BaseDirectory);
    }
}
