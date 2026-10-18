using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace OpenSkyAtc.SimConnect;

/// <summary>
/// Finds and loads the SimConnect DLLs: the copies the installer puts next to the app, else the user's MSFS 2024 SDK
/// (developer builds reference them with Private=false, so they are never in the build output). The source
/// repository never contains them (see LICENSE-EXCEPTION.md). Must run before any SimConnect type is used.
/// </summary>
public static class SimConnectLibrary
{
    public const string ManagedFileName = "Microsoft.FlightSimulator.SimConnect.dll";
    public const string NativeFileName = "SimConnect.dll";
    public const string SdkEnvironmentVariable = "MSFS2024_SDK";

    private static string? _managedPath;

    public static bool IsLoaded => _managedPath is not null;

    /// <summary>
    /// Locates both DLLs. <paramref name="directory"/> may be the SDK root, its "SimConnect SDK\lib"
    /// folder, or any folder holding both files. When null, the folders from <see cref="Candidates"/> are tried.
    /// </summary>
    public static (string Managed, string Native)? Locate(string? directory = null)
    {
        if (directory is null)
        {
            foreach (var candidate in Candidates())
                if (Locate(candidate) is { } hit) return hit;
            return null;
        }
        if (string.IsNullOrWhiteSpace(directory)) return null;

        string[] roots =
        [
            directory,
            Path.Combine(directory, "lib"),
            Path.Combine(directory, "SimConnect SDK", "lib"),
        ];
        foreach (var root in roots)
        {
            string native = Path.Combine(root, NativeFileName);
            foreach (var managed in new[] { Path.Combine(root, ManagedFileName), Path.Combine(root, "managed", ManagedFileName) })
            {
                if (File.Exists(managed) && File.Exists(native)) return (managed, native);
            }
        }
        return null;
    }

    /// <summary>The app's own folder (an installed copy ships both DLLs there), then the SDK folders.</summary>
    public static IEnumerable<string> Candidates() => SdkCandidates().Prepend(AppContext.BaseDirectory);

    /// <summary>
    /// The MSFS2024_SDK variable of this process, then the machine and user values the SDK installer wrote (a process
    /// started before the install does not see them), then the installer's default folder.
    /// </summary>
    public static IEnumerable<string> SdkCandidates() =>
        new[]
        {
            Environment.GetEnvironmentVariable(SdkEnvironmentVariable),
            Environment.GetEnvironmentVariable(SdkEnvironmentVariable, EnvironmentVariableTarget.Machine),
            Environment.GetEnvironmentVariable(SdkEnvironmentVariable, EnvironmentVariableTarget.User),
            @"C:\MSFS 2024 SDK",
        }
        .Where(d => !string.IsNullOrWhiteSpace(d))
        .Select(d => d!)
        .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Loads the DLLs. Throws <see cref="FileNotFoundException"/> with instructions when they cannot be found.</summary>
    public static void EnsureLoaded(string? directory = null)
    {
        if (IsLoaded) return;

        var found = Locate(directory) ?? throw new FileNotFoundException(
            $"Could not find the SimConnect DLLs ({ManagedFileName} and {NativeFileName}). Reinstall OpenSkyATC, " +
            $"or install the Microsoft Flight Simulator 2024 SDK (it sets the {SdkEnvironmentVariable} environment variable).");

        // The managed assembly imports the native DLL by name; once loaded, Windows reuses it.
        NativeLibrary.Load(found.Native);
        _managedPath = found.Managed;
        AssemblyLoadContext.Default.Resolving += Resolve;
    }

    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name) =>
        name.Name == Path.GetFileNameWithoutExtension(ManagedFileName) && _managedPath is not null
            ? context.LoadFromAssemblyPath(_managedPath)
            : null;
}
