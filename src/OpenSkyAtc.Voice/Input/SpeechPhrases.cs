namespace OpenSkyAtc.Voice.Input;

/// <summary>A pilot call from the speech test list: its id (the WAV file name) and the words, written as spoken.</summary>
public sealed record SpeechPhrase(string Id, string Text);

/// <summary>Reads tests/fixtures/speech/phrases.txt: "id | text" per line, # comments.</summary>
public static class SpeechPhrases
{
    public static IReadOnlyList<SpeechPhrase> Parse(string text) =>
        text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split('|', 2, StringSplitOptions.TrimEntries) is [var id, var words] && id.Length > 0
                ? new SpeechPhrase(id, words)
                : throw new FormatException($"Bad phrase line: '{l}'"))
            .ToList();

    public static IReadOnlyList<SpeechPhrase> Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>
    /// Word error rate of <paramref name="heard"/> against <paramref name="said"/>, both compared in
    /// <see cref="SpokenText"/> form (so "121.635" and "one two one decimal six three five" are the same words).
    /// </summary>
    public static double WordErrorRate(string said, string heard)
    {
        var r = SpokenText.Normalize(said).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var h = SpokenText.Normalize(heard).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (r.Length == 0) return h.Length == 0 ? 0 : 1;
        var d = new int[r.Length + 1, h.Length + 1];
        for (int i = 0; i <= r.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= h.Length; j++) d[0, j] = j;
        for (int i = 1; i <= r.Length; i++)
            for (int j = 1; j <= h.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (r[i - 1] == h[j - 1] ? 0 : 1));
        return (double)d[r.Length, h.Length] / r.Length;
    }
}
