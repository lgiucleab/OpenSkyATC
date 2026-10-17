namespace OpenSkyAtc.Voice.Input;

/// <summary>An English-only Whisper model in ggml format, from the whisper.cpp model page (MIT).</summary>
/// <param name="Id">Setting value and file stem: "base.en" is ggml-base.en.bin.</param>
/// <param name="SizeMb">Download size, shown before downloading.</param>
public sealed record WhisperModel(string Id, string Label, int SizeMb)
{
    public string FileName => $"ggml-{Id}.bin";
    public Uri DownloadUrl => new($"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{FileName}");
}

public static class WhisperModels
{
    public static readonly WhisperModel Tiny = new("tiny.en", "Tiny (fastest)", 75);
    public static readonly WhisperModel Base = new("base.en", "Base", 142);
    public static readonly WhisperModel BaseQ5 = new("base.en-q5_1", "Base, compressed", 57);
    public static readonly WhisperModel Small = new("small.en", "Small (most accurate)", 466);
    public static readonly WhisperModel SmallQ5 = new("small.en-q5_1", "Small, compressed", 181);

    public static readonly IReadOnlyList<WhisperModel> All = [Tiny, BaseQ5, Base, SmallQ5, Small];

    public static WhisperModel? Find(string? id) => All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Whisper models on disk, %LOCALAPPDATA%\OpenSkyATC\whisper; downloaded on first use like the voices.</summary>
public sealed class WhisperModelStore(string? root = null)
{
    public string Root { get; } = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenSkyATC", "whisper");

    public string PathOf(WhisperModel model) => Path.Combine(Root, model.FileName);

    /// <summary>Present and at least half the expected size (a cut download is never renamed into place).</summary>
    public bool IsInstalled(WhisperModel model) =>
        File.Exists(PathOf(model)) && new FileInfo(PathOf(model)).Length > model.SizeMb * 1024L * 1024 / 2;

    public async Task InstallAsync(WhisperModel model, HttpClient http, IProgress<(long Received, long? Total)>? progress = null, CancellationToken cancel = default)
    {
        if (IsInstalled(model)) return;
        Directory.CreateDirectory(Root);
        string partial = PathOf(model) + ".part";
        using (var response = await http.GetAsync(model.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancel))
        {
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancel);
            await using var file = File.Create(partial);
            var buffer = new byte[1 << 16];
            long received = 0;
            int n;
            while ((n = await source.ReadAsync(buffer, cancel)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, n), cancel);
                received += n;
                progress?.Report((received, total));
            }
        }
        File.Move(partial, PathOf(model), overwrite: true);
    }
}
