namespace QuickShell.Services;

internal static class JetBrainsInstallDiscovery
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?> ProductCache =
        new(StringComparer.OrdinalIgnoreCase);

    public static void InvalidateCache() => ProductCache.Clear();

    public static string? TryResolveRider() =>
        ResolveCached(
            "Rider",
            () => TryResolveProduct(
                ["Rider"],
                "rider64.exe",
                directoryName => directoryName.Contains("Rider", StringComparison.OrdinalIgnoreCase)));

    public static string? TryResolveIntelliJIdea() =>
        ResolveCached(
            "IntelliJ",
            () => TryResolveProduct(
                ["IDEA-U", "IDEA-C", "IntelliJ IDEA"],
                "idea64.exe",
                directoryName => directoryName.Contains("IntelliJ", StringComparison.OrdinalIgnoreCase)
                    || directoryName.StartsWith("IDEA", StringComparison.OrdinalIgnoreCase)));

    public static string? TryResolveWebStorm() =>
        ResolveCached(
            "WebStorm",
            () => TryResolveProduct(
                ["WebStorm"],
                "webstorm64.exe",
                directoryName => directoryName.Contains("WebStorm", StringComparison.OrdinalIgnoreCase)));

    public static string? TryResolvePyCharm() =>
        ResolveCached(
            "PyCharm",
            () => TryResolveProduct(
                ["PyCharm-P", "PyCharm-C", "PyCharm"],
                "pycharm64.exe",
                directoryName => directoryName.Contains("PyCharm", StringComparison.OrdinalIgnoreCase)));

    public static string? TryResolveGoLand() =>
        ResolveCached(
            "GoLand",
            () => TryResolveProduct(
                ["Goland", "GoLand"],
                "goland64.exe",
                directoryName => directoryName.Contains("GoLand", StringComparison.OrdinalIgnoreCase)
                    || directoryName.Contains("Goland", StringComparison.OrdinalIgnoreCase)));

    public static string? TryResolveCLion() =>
        ResolveCached(
            "CLion",
            () => TryResolveProduct(
                ["CLion"],
                "clion64.exe",
                directoryName => directoryName.Contains("CLion", StringComparison.OrdinalIgnoreCase)));

    public static string? TryResolveAndroidStudio() =>
        ResolveCached(
            "AndroidStudio",
            () => TryResolveProduct(
                    ["AndroidStudio", "AndroidStudioPreview"],
                    "studio64.exe",
                    directoryName => directoryName.Contains("Android Studio", StringComparison.OrdinalIgnoreCase))
                ?? TryResolveAndroidStudioProgramFiles());

    private static string? ResolveCached(string key, Func<string?> resolve)
    {
        if (ProductCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var resolved = resolve();
        ProductCache[key] = resolved;
        return resolved;
    }

    private static string? TryResolveAndroidStudioProgramFiles()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        foreach (var root in new[]
                 {
                     Path.Combine(programFiles, "Android", "Android Studio", "bin", "studio64.exe"),
                     Path.Combine(programFilesX86, "Android", "Android Studio", "bin", "studio64.exe"),
                     Path.Combine(programFiles, "Google", "Android Studio", "bin", "studio64.exe"),
                 })
        {
            if (File.Exists(root))
            {
                return Path.GetFullPath(root);
            }
        }

        return null;
    }

    private static string? TryResolveProduct(
        IReadOnlyList<string> toolboxAppFolders,
        string executableName,
        Func<string, bool> matchesStandaloneFolder)
    {
        foreach (var toolboxAppFolder in toolboxAppFolders)
        {
            var fromToolbox = TryResolveFromToolbox(toolboxAppFolder, executableName);
            if (fromToolbox is not null)
            {
                return fromToolbox;
            }
        }

        return TryResolveFromProgramFiles(executableName, matchesStandaloneFolder);
    }

    private static string? TryResolveFromToolbox(string appFolder, string executableName)
    {
        var toolboxApps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JetBrains",
            "Toolbox",
            "apps",
            appFolder);
        if (!Directory.Exists(toolboxApps))
        {
            return null;
        }

        try
        {
            FileInfo? newest = null;
            foreach (var channel in Directory.EnumerateDirectories(toolboxApps, "ch-*", SearchOption.TopDirectoryOnly))
            {
                foreach (var executable in FindToolboxExecutables(channel, executableName))
                {
                    if (newest is null || executable.LastWriteTimeUtc > newest.LastWriteTimeUtc)
                    {
                        newest = executable;
                    }
                }
            }

            return newest?.FullName;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<FileInfo> FindToolboxExecutables(string channelDirectory, string executableName)
    {
        var direct = Path.Combine(channelDirectory, "bin", executableName);
        if (File.Exists(direct))
        {
            yield return new FileInfo(direct);
        }

        IEnumerable<string> buildDirectories;
        try
        {
            buildDirectories = Directory.EnumerateDirectories(channelDirectory);
        }
        catch
        {
            yield break;
        }

        foreach (var buildDirectory in buildDirectories)
        {
            var nested = Path.Combine(buildDirectory, "bin", executableName);
            if (File.Exists(nested))
            {
                yield return new FileInfo(nested);
            }
        }
    }

    private static string? TryResolveFromProgramFiles(string executableName, Func<string, bool> matchesFolder)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var jetBrainsRoot = Path.Combine(programFiles, "JetBrains");
        if (!Directory.Exists(jetBrainsRoot))
        {
            return null;
        }

        try
        {
            FileInfo? newest = null;
            foreach (var productDirectory in Directory.EnumerateDirectories(jetBrainsRoot))
            {
                var folderName = Path.GetFileName(productDirectory);
                if (!matchesFolder(folderName))
                {
                    continue;
                }

                var executable = Path.Combine(productDirectory, "bin", executableName);
                if (!File.Exists(executable))
                {
                    continue;
                }

                var candidate = new FileInfo(executable);
                if (newest is null || candidate.LastWriteTimeUtc > newest.LastWriteTimeUtc)
                {
                    newest = candidate;
                }
            }

            return newest?.FullName;
        }
        catch
        {
            return null;
        }
    }
}
