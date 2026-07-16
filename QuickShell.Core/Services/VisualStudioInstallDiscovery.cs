using System.Diagnostics;

namespace QuickShell.Services;

internal static class VisualStudioInstallDiscovery
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(int Min, int Max), string?> DevenvCache = new();

    public static string? TryResolveDevenv(int minVersionInclusive, int maxVersionExclusive)
    {
        var key = (minVersionInclusive, maxVersionExclusive);
        if (DevenvCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var installationPath = TryQueryInstallationPath(minVersionInclusive, maxVersionExclusive);
        string? resolved = null;
        if (!string.IsNullOrWhiteSpace(installationPath))
        {
            var devenv = Path.Combine(installationPath, "Common7", "IDE", "devenv.exe");
            if (File.Exists(devenv))
            {
                resolved = Path.GetFullPath(devenv);
            }
        }

        DevenvCache[key] = resolved;
        return resolved;
    }

    public static void InvalidateCache() => DevenvCache.Clear();

    public static string? TryInferPresetFromDevenvPath(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        var path = executablePath.Trim();
        if (!string.Equals(Path.GetFileName(path), "devenv.exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (CompanionAppCatalog.TryResolveExecutablePath(path, out var resolved))
        {
            foreach (var (presetId, min, max) in PresetVersionRanges)
            {
                var devenv = TryResolveDevenv(min, max);
                if (devenv is not null
                    && string.Equals(devenv, resolved, StringComparison.OrdinalIgnoreCase))
                {
                    return presetId;
                }
            }
        }

        var normalized = path.Replace('/', '\\');
        if (normalized.Contains(@"\2022\", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Visual Studio 2022", StringComparison.OrdinalIgnoreCase))
        {
            return CompanionAppCatalog.PresetVs2022;
        }

        if (normalized.Contains(@"\2026\", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Visual Studio 2026", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(@"\18.", StringComparison.OrdinalIgnoreCase))
        {
            return CompanionAppCatalog.PresetVs2026;
        }

        return CompanionAppCatalog.PresetCustom;
    }

    private static IEnumerable<(string PresetId, int Min, int Max)> PresetVersionRanges =>
    [
        (CompanionAppCatalog.PresetVs2026, 18, 19),
        (CompanionAppCatalog.PresetVs2022, 17, 18),
    ];

    private static string? TryQueryInstallationPath(int minVersionInclusive, int maxVersionExclusive)
    {
        var vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio",
            "Installer",
            "vswhere.exe");
        if (!File.Exists(vswhere))
        {
            return null;
        }

        var versionRange = $"[{minVersionInclusive}.0,{maxVersionExclusive}.0)";
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = vswhere,
                Arguments = $"-version {versionRange} -latest -property installationPath",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(5000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best effort.
                }

                return null;
            }

            var output = outputTask.GetAwaiter().GetResult().Trim();
            _ = errorTask.GetAwaiter().GetResult();
            return process.ExitCode != 0 || string.IsNullOrWhiteSpace(output) || !Directory.Exists(output)
                ? null
                : output;
        }
        catch
        {
            return null;
        }
    }
}
