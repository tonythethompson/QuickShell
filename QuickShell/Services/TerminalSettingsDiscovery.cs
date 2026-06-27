namespace QuickShell.Services;

internal static class TerminalSettingsDiscovery
{
    private sealed record PackageRule(
        string PackageNameFragment,
        TerminalSettingsSource Source,
        string HostExecutable,
        string IdPrefix,
        string DisplayPrefix,
        string[]? ExcludeFragments = null)
    {
        public IReadOnlyList<string> ExcludedPackageFragments { get; } =
            ExcludeFragments ?? [];
    }

    private static readonly PackageRule[] PackageRules =
    [
        new(
            "IntelligentTerminal",
            TerminalSettingsSource.IntelligentTerminal,
            "wtai.exe",
            "it",
            "Intelligent Terminal"),
        new(
            "WindowsTerminalPreview",
            TerminalSettingsSource.WindowsTerminalPreview,
            "wt.exe",
            "wtp",
            "Windows Terminal Preview"),
        new(
            "WindowsTerminal",
            TerminalSettingsSource.WindowsTerminal,
            "wt.exe",
            "wt",
            "Windows Terminal",
            ExcludeFragments: ["Preview", "Intelligent"]),
    ];

    public static IReadOnlyList<TerminalSettingsLocation> DiscoverLocations()
    {
        var locations = new List<TerminalSettingsLocation>();
        var seenSettingsPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddUnpackagedLocation(locations, seenSettingsPaths);
        AddPackageLocations(locations, seenSettingsPaths);

        return locations;
    }

    private static void AddUnpackagedLocation(
        List<TerminalSettingsLocation> locations,
        HashSet<string> seenSettingsPaths)
    {
        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "Windows Terminal",
            "settings.json");

        if (!File.Exists(settingsPath) || !seenSettingsPaths.Add(settingsPath))
        {
            return;
        }

        locations.Add(new TerminalSettingsLocation
        {
            SettingsPath = settingsPath,
            Source = TerminalSettingsSource.Unpackaged,
            HostExecutable = "wt.exe",
            IdPrefix = "wtu",
            DisplayPrefix = "Windows Terminal",
        });
    }

    private static void AddPackageLocations(
        List<TerminalSettingsLocation> locations,
        HashSet<string> seenSettingsPaths)
    {
        var packagesRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages");

        if (!Directory.Exists(packagesRoot))
        {
            return;
        }

        foreach (var packageDirectory in Directory.EnumerateDirectories(packagesRoot))
        {
            var packageFolder = Path.GetFileName(packageDirectory);
            if (string.IsNullOrWhiteSpace(packageFolder)
                || !packageFolder.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var settingsPath = Path.Combine(packageDirectory, "LocalState", "settings.json");
            if (!File.Exists(settingsPath) || !seenSettingsPaths.Add(settingsPath))
            {
                continue;
            }

            if (!TryResolvePackageRule(packageFolder, out var rule))
            {
                continue;
            }

            locations.Add(new TerminalSettingsLocation
            {
                SettingsPath = settingsPath,
                Source = rule.Source,
                HostExecutable = rule.HostExecutable,
                IdPrefix = rule.IdPrefix,
                DisplayPrefix = rule.DisplayPrefix,
            });
        }
    }

    private static bool TryResolvePackageRule(string packageFolder, out PackageRule rule)
    {
        foreach (var candidate in PackageRules)
        {
            if (!PackageFolderMatchesRule(packageFolder, candidate))
            {
                continue;
            }

            rule = candidate;
            return true;
        }

        rule = null!;
        return false;
    }

    private static bool PackageFolderMatchesRule(string packageFolder, PackageRule rule)
    {
        if (!packageFolder.Contains(rule.PackageNameFragment, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var excluded in rule.ExcludedPackageFragments)
        {
            if (packageFolder.Contains(excluded, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
