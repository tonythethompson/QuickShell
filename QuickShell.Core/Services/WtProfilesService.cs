using System.Text.Json;

namespace QuickShell.Services;

internal sealed class WtProfileInfo
{
    public required string Name { get; init; }

    public string? Guid { get; init; }

    public string? Commandline { get; init; }

    public string? Icon { get; init; }

    public string? ProfileSource { get; init; }

    public required string SettingsPath { get; init; }

    public bool IsDefault { get; init; }

    public required TerminalSettingsSource Source { get; init; }

    public required string HostExecutable { get; init; }

    public required string IdPrefix { get; init; }

    public required string SourceLabel { get; init; }
}

internal static class WtProfilesService
{
    private static readonly object Sync = new();

    private static WtProfileInfo[] _cached = [];
    private static readonly Dictionary<string, DateTime> _writeTimes = new(StringComparer.OrdinalIgnoreCase);
    private static TerminalSettingsLocation[] _locations = [];

    public static void InvalidateCache()
    {
        lock (Sync)
        {
            _cached = [];
            _writeTimes.Clear();
            _locations = [];
        }

        WindowsTerminalInstallDiscovery.InvalidateCache();
    }

    private static TerminalSettingsLocation[] GetLocations()
    {
        if (_locations.Length == 0)
        {
            _locations = [.. TerminalSettingsDiscovery.DiscoverLocations()];
        }

        return _locations;
    }

    public static IReadOnlyList<WtProfileInfo> GetProfiles()
    {
        lock (Sync)
        {
            RefreshCacheIfNeeded();
            return _cached;
        }
    }

    public static IReadOnlyList<string> GetProfileNames() =>
        GetProfiles().Select(p => p.Name).ToArray();

    public static WtProfileInfo? FindProfileForLaunch(string? terminal, string? profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return null;
        }

        var prefixes = GetIdPrefixesForTerminal(terminal);
        if (prefixes.Length == 0)
        {
            return null;
        }

        var trimmedName = profileName.Trim();
        return GetProfiles().FirstOrDefault(profile =>
            prefixes.Contains(profile.IdPrefix, StringComparer.OrdinalIgnoreCase)
            && profile.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase));
    }

    public static WtProfileInfo? FindProfileByNameAcrossHosts(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return null;
        }

        var trimmedName = profileName.Trim();
        return GetProfiles().FirstOrDefault(profile =>
            profile.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase));
    }

    public static WtProfileInfo? FindDefaultProfile(string hostTerminal)
    {
        var prefixes = GetIdPrefixesForTerminal(hostTerminal);
        if (prefixes.Length == 0)
        {
            return null;
        }

        foreach (var location in GetLocations())
        {
            if (!prefixes.Contains(location.IdPrefix, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var defaultGuid = ReadDefaultProfileGuid(location.SettingsPath);
            if (string.IsNullOrWhiteSpace(defaultGuid))
            {
                continue;
            }

            var match = GetProfiles().FirstOrDefault(profile =>
                profile.IdPrefix.Equals(location.IdPrefix, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(profile.Guid)
                && profile.Guid.Equals(defaultGuid, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                return match;
            }
        }

        return GetProfiles().FirstOrDefault(profile =>
            prefixes.Contains(profile.IdPrefix, StringComparer.OrdinalIgnoreCase)
            && profile.IsDefault);
    }

    public static WtProfileInfo? FindProfileForStandaloneShell(string shellId)
    {
        var normalized = (shellId ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "powershell7" => "pwsh",
            _ => (shellId ?? string.Empty).Trim().ToLowerInvariant(),
        };

        return GetProfiles().FirstOrDefault(profile => MatchesStandaloneShell(profile, normalized));
    }

    public static IReadOnlyList<WtProfileInfo> GetProfilesForApplication(string terminalApplicationId)
    {
        if (terminalApplicationId.Equals(TerminalHostIds.IntelligentTerminal, StringComparison.OrdinalIgnoreCase))
        {
            return GetProfiles()
                .Where(p => p.IdPrefix.Equals(TerminalHostIds.IntelligentTerminal, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        return GetProfiles()
            .Where(p => TerminalHostIds.IsWindowsTerminalProfilePrefix(p.IdPrefix))
            .ToArray();
    }

    private static void RefreshCacheIfNeeded()
    {
        var merged = new List<WtProfileInfo>();
        var sawChanges = _cached.Length == 0;
        var locations = GetLocations();

        foreach (var location in locations)
        {
            if (!File.Exists(location.SettingsPath))
            {
                continue;
            }

            var writeTime = File.GetLastWriteTimeUtc(location.SettingsPath);
            if (_writeTimes.TryGetValue(location.SettingsPath, out var cachedTime)
                && cachedTime == writeTime
                && !sawChanges)
            {
                continue;
            }

            sawChanges = true;
            _writeTimes[location.SettingsPath] = writeTime;
        }

        if (!sawChanges)
        {
            return;
        }

        foreach (var location in locations)
        {
            if (!File.Exists(location.SettingsPath))
            {
                continue;
            }

            merged.AddRange(TryReadProfiles(location));
        }

        _cached = merged
            .GroupBy(p => $"{p.IdPrefix}:{p.Name}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.SourceLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _cached = MergeIconsAcrossProfiles(_cached);
    }

    private static WtProfileInfo[] MergeIconsAcrossProfiles(WtProfileInfo[] profiles)
    {
        var iconsByGuid = profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Guid) && !string.IsNullOrWhiteSpace(profile.Icon))
            .GroupBy(profile => NormalizeGuid(profile.Guid!), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Icon!, StringComparer.OrdinalIgnoreCase);

        if (iconsByGuid.Count == 0)
        {
            return profiles;
        }

        return profiles
            .Select(profile =>
            {
                if (!string.IsNullOrWhiteSpace(profile.Icon)
                    || string.IsNullOrWhiteSpace(profile.Guid)
                    || !iconsByGuid.TryGetValue(NormalizeGuid(profile.Guid), out var icon))
                {
                    return profile;
                }

                return new WtProfileInfo
                {
                    Name = profile.Name,
                    Guid = profile.Guid,
                    Commandline = profile.Commandline,
                    Icon = icon,
                    ProfileSource = profile.ProfileSource,
                    SettingsPath = profile.SettingsPath,
                    IsDefault = profile.IsDefault,
                    Source = profile.Source,
                    HostExecutable = profile.HostExecutable,
                    IdPrefix = profile.IdPrefix,
                    SourceLabel = profile.SourceLabel,
                };
            })
            .ToArray();
    }

    private static IEnumerable<WtProfileInfo> TryReadProfiles(TerminalSettingsLocation location)
    {
        if (!File.Exists(location.SettingsPath))
        {
            yield break;
        }

        WtProfileInfo[] profiles;
        try
        {
            using var stream = File.OpenRead(location.SettingsPath);
            using var doc = JsonDocument.Parse(stream);

            var defaultGuid = ReadDefaultProfileGuid(doc.RootElement);
            if (!doc.RootElement.TryGetProperty("profiles", out var profilesNode))
            {
                yield break;
            }

            var listNode = profilesNode.TryGetProperty("list", out var directList)
                ? directList
                : profilesNode;

            if (listNode.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            profiles = listNode
                .EnumerateArray()
                .Select(element => ToProfile(element, defaultGuid, location))
                .Where(p => p is not null)
                .Cast<WtProfileInfo>()
                .ToArray();
        }
        catch
        {
            yield break;
        }

        foreach (var profile in profiles)
        {
            yield return profile;
        }
    }

    private static string? ReadDefaultProfileGuid(JsonElement root)
    {
        if (root.TryGetProperty("defaultProfile", out var topLevel) && topLevel.ValueKind == JsonValueKind.String)
        {
            return topLevel.GetString();
        }

        if (root.TryGetProperty("profiles", out var profilesNode)
            && profilesNode.TryGetProperty("defaultProfile", out var nested)
            && nested.ValueKind == JsonValueKind.String)
        {
            return nested.GetString();
        }

        return null;
    }

    private static WtProfileInfo? ToProfile(JsonElement element, string? defaultGuid, TerminalSettingsLocation location)
    {
        if (!element.TryGetProperty("name", out var nameNode))
        {
            return null;
        }

        var name = nameNode.GetString();
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (element.TryGetProperty("hidden", out var hiddenNode)
            && hiddenNode.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        if (name.Equals("Agent Pane", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var guid = element.TryGetProperty("guid", out var guidNode) ? guidNode.GetString() : null;
        var commandline = element.TryGetProperty("commandline", out var commandNode)
            ? commandNode.GetString()
            : null;
        var icon = element.TryGetProperty("icon", out var iconNode)
            ? iconNode.GetString()
            : null;
        var profileSource = element.TryGetProperty("source", out var sourceNode)
            ? sourceNode.GetString()
            : null;

        return new WtProfileInfo
        {
            Name = name.Trim(),
            Guid = guid,
            Commandline = commandline,
            Icon = icon,
            ProfileSource = profileSource,
            SettingsPath = location.SettingsPath,
            IsDefault = !string.IsNullOrWhiteSpace(defaultGuid)
                && !string.IsNullOrWhiteSpace(guid)
                && defaultGuid.Equals(guid, StringComparison.OrdinalIgnoreCase),
            Source = location.Source,
            HostExecutable = location.HostExecutable,
            IdPrefix = location.IdPrefix,
            SourceLabel = location.DisplayPrefix,
        };
    }

    private static string[] GetIdPrefixesForTerminal(string? terminal) =>
        (terminal ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "it" or "intelligent-terminal" => [TerminalHostIds.IntelligentTerminal],
            "wt" or "windows-terminal" =>
            [
                TerminalHostIds.WindowsTerminal,
                "wtu",
                "wtp",
            ],
            _ => [],
        };

    private static string? ReadDefaultProfileGuid(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
            {
                return null;
            }

            using var stream = File.OpenRead(settingsPath);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.TryGetProperty("defaultProfile", out var topLevel)
                && topLevel.ValueKind == JsonValueKind.String)
            {
                return topLevel.GetString();
            }

            if (document.RootElement.TryGetProperty("profiles", out var profilesNode)
                && profilesNode.TryGetProperty("defaultProfile", out var nested)
                && nested.ValueKind == JsonValueKind.String)
            {
                return nested.GetString();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static bool MatchesStandaloneShell(WtProfileInfo profile, string shellId) =>
        shellId switch
        {
            "pwsh" => ContainsIgnoreCase(profile.Commandline, "pwsh")
                || ContainsIgnoreCase(profile.Name, "PowerShell 7")
                || ContainsIgnoreCase(profile.ProfileSource, "PowershellCore"),
            "powershell" => ContainsIgnoreCase(profile.Commandline, "powershell.exe")
                || profile.Name.Equals("Windows PowerShell", StringComparison.OrdinalIgnoreCase)
                || ContainsIgnoreCase(profile.ProfileSource, "Windows.Terminal.Powershell"),
            "cmd" => ContainsIgnoreCase(profile.Commandline, "cmd.exe")
                || profile.Name.Equals("Command Prompt", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

    private static bool ContainsIgnoreCase(string? value, string fragment) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeGuid(string guid) => guid.Trim('{', '}');
}
