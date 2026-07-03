namespace QuickShell.Services;

internal static class WindowsTerminalInstallDiscovery
{
    private static readonly object Sync = new();
    private static string[] _cached = [];

    public static void InvalidateCache()
    {
        lock (Sync)
        {
            _cached = [];
        }
    }

    public static IReadOnlyList<string> GetInstallPaths()
    {
        lock (Sync)
        {
            if (_cached.Length > 0)
            {
                return _cached;
            }

            var paths = new List<string>();
            TryAddFromAppxPackages(paths);
            TryAddFromWindowsApps(paths);
            _cached = paths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return _cached;
        }
    }

    private static void TryAddFromAppxPackages(List<string> paths)
    {
        foreach (var packageName in new[]
                 {
                     "Microsoft.WindowsTerminal",
                     "Microsoft.WindowsTerminalPreview",
                     "Microsoft.IntelligentTerminal",
                 })
        {
            var installLocation = TryGetAppxInstallLocation(packageName);
            if (!string.IsNullOrWhiteSpace(installLocation))
            {
                paths.Add(installLocation);
            }
        }
    }

    private static string? TryGetAppxInstallLocation(string packageName)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"(Get-AppxPackage {packageName} | Select-Object -First 1 -ExpandProperty InstallLocation)\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null || !process.WaitForExit(5000) || process.ExitCode != 0)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            return string.IsNullOrWhiteSpace(output) || !Directory.Exists(output)
                ? null
                : output;
        }
        catch
        {
            return null;
        }
    }

    private static void TryAddFromWindowsApps(List<string> paths)
    {
        var programFilesRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "WindowsApps");

        if (!Directory.Exists(programFilesRoot))
        {
            return;
        }

        try
        {
            foreach (var directory in Directory.EnumerateDirectories(programFilesRoot))
            {
                var name = Path.GetFileName(directory);
                if (IsTerminalPackageFolder(name))
                {
                    paths.Add(directory);
                }
            }
        }
        catch
        {
            // WindowsApps may block enumeration for some entries.
        }
    }

    private static bool IsTerminalPackageFolder(string name) =>
        name.StartsWith("Microsoft.WindowsTerminal_", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Microsoft.WindowsTerminalPreview_", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Microsoft.IntelligentTerminal_", StringComparison.OrdinalIgnoreCase);
}
