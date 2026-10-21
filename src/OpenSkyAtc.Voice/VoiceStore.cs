using System.Formats.Tar;
using ICSharpCode.SharpZipLib.BZip2;

namespace OpenSkyAtc.Voice;

/// <summary>
/// Voice models on disk. The installer ships them next to the app in voices\&lt;archive&gt; (owner's choice, refactor
/// phase 4: everything comes with the installer), sharing one espeak-ng-data folder; a model that is not there (a
/// development build) is downloaded on first use into %LOCALAPPDATA%\OpenSkyATC\voices\&lt;archive&gt;. Never committed.
/// </summary>
public sealed class VoiceStore(string? root = null, string? bundled = null)
{
    public string Root { get; } = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenSkyATC", "voices");

    /// <summary>The installer's read-only voices folder (none when a test gives its own root).</summary>
    public string? Bundled { get; } = bundled ?? (root is null ? Path.Combine(AppContext.BaseDirectory, "voices") : null);

    /// <summary>Where the model is: the bundled copy when there is one, else the downloaded one.</summary>
    public string FolderOf(VoiceModel model) =>
        Bundled is { } b && Directory.Exists(Path.Combine(b, model.Archive)) ? Path.Combine(b, model.Archive) : Path.Combine(Root, model.Archive);

    /// <summary>The model's espeak-ng-data: its own, else the installer's shared copy.</summary>
    public string DataDirOf(VoiceModel model)
    {
        string own = Path.Combine(FolderOf(model), "espeak-ng-data");
        if (Directory.Exists(own) || Bundled is null) return own;
        string shared = Path.Combine(Bundled, "espeak-ng-data");
        return Directory.Exists(shared) ? shared : own;
    }

    public bool IsInstalled(VoiceModel model) => ModelFile(model) is not null && File.Exists(Path.Combine(FolderOf(model), "tokens.txt"));

    public IReadOnlyList<VoiceModel> Installed() => VoiceCatalog.All.Where(IsInstalled).ToList();

    /// <summary>The model's .onnx file, or null when it is not installed.</summary>
    public string? ModelFile(VoiceModel model)
    {
        string dir = FolderOf(model);
        return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.onnx").FirstOrDefault() : null;
    }

    /// <summary>Downloads and unpacks a model unless it is already there. Reports progress as bytes received / total (total may be null).</summary>
    public async Task InstallAsync(VoiceModel model, HttpClient http, IProgress<(long Received, long? Total)>? progress = null, CancellationToken cancel = default)
    {
        if (IsInstalled(model)) return;
        Directory.CreateDirectory(Root);
        string partial = Path.Combine(Root, model.Archive + ".tar.bz2.part");

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

        // The archive holds one folder named like the archive: model .onnx, tokens.txt, espeak-ng-data, MODEL_CARD.
        string staging = Path.Combine(Root, model.Archive + ".unpacking");
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);
        await using (var compressed = File.OpenRead(partial))
        await using (var tar = new BZip2InputStream(compressed))
        {
            await TarFile.ExtractToDirectoryAsync(tar, staging, overwriteFiles: true, cancel);
        }
        string unpacked = Path.Combine(staging, model.Archive);
        if (!Directory.Exists(unpacked)) unpacked = staging;
        string target = Path.Combine(Root, model.Archive); // downloads always go to the user folder
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        Directory.Move(unpacked, target);
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        File.Delete(partial);
        if (!IsInstalled(model)) throw new InvalidDataException($"The {model.Id} voice archive did not contain a model.");
    }
}
