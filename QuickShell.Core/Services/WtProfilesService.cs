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

    /// <summary>
    /// When the profile list is warm, skip re-statting every settings path on every call.
    /// Terminal resolution can hit <see cref="GetProfiles"/> many times per form/launch.
    /// </summary>
    private const int RefreshCheckMinIntervalMs = 2000;

    private static WtProfileInfo[] _cached = [];
    private static readonly Dictionary<string, DateTime> _writeTimes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, WtProfileInfo[]> _profilesBySettingsPath = new(StringComparer.OrdinalIgnoreCase);
    private static TerminalSettingsLocation[] _locations = [];
    private static long _lastRefreshCheckTickMs;

    internal static TerminalSettingsLocation[]? TestLocationsOverride { get; set; }

    internal static int TestParseCount { get; private set; }

    internal static Action? TestOnParseForTests { get; set; }

    public static void InvalidateCache()
    {
        lock (Sync)
        {
            _cached = [];
            _writeTimes.Clear();
            _profilesBySettingsPath.Clear();
            _locations = [];
            _lastRefreshCheckTickMs = 0;
            TestLocationsOverride = null;
            TestOnParseForTests = null;
            TestParseCount = 0;
        }

        WindowsTerminalInstallDiscovery.InvalidateCache();
    }

    internal sealed class TestScope : IDisposable
    {
        public TestScope(TerminalSettingsLocation[] locations, Action? onParse = null)
        {
            lock (Sync)
            {
                TestLocationsOverride = locations;
                TestOnParseForTests = onParse;
            }
        }

        public void Dispose()
        {
            lock (Sync)
            {
                TestLocationsOverride = null;
                TestOnParseForTests = null;
            }
        }
    }

    private static TerminalSettingsLocation[] GetLocations()
    {
        if (TestLocationsOverride is { Length: > 0 } overrideLocations)
        {
            return overrideLocations;
        }

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
        var forceRefresh = _cached.Length == 0 && _writeTimes.Count == 0 && _profilesBySettingsPath.Count == 0;
        var nowTick = Environment.TickCount64;

        // Warm cache + production (not test scope): reuse last merge without re-statting
        // every known settings.json on each terminal-target resolution.
        if (!forceRefresh
            && _cached.Length > 0
            && TestLocationsOverride is null
            && nowTick - _lastRefreshCheckTickMs < RefreshCheckMinIntervalMs)
        {
            return;
        }

        _lastRefreshCheckTickMs = nowTick;
        var sawChanges = forceRefresh;
        var locations = GetLocations();
        var activePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var location in locations)
        {
            activePaths.Add(location.SettingsPath);

            if (!File.Exists(location.SettingsPath))
            {
                if (_profilesBySettingsPath.Remove(location.SettingsPath))
                {
                    sawChanges = true;
                }

                _writeTimes.Remove(location.SettingsPath);
                continue;
            }

            var writeTime = File.GetLastWriteTimeUtc(location.SettingsPath);
            if (!forceRefresh
                && _writeTimes.TryGetValue(location.SettingsPath, out var cachedTime)
                && cachedTime == writeTime
                && _profilesBySettingsPath.ContainsKey(location.SettingsPath))
            {
                continue;
            }

            sawChanges = true;
            _writeTimes[location.SettingsPath] = writeTime;
            _profilesBySettingsPath[location.SettingsPath] = ReadProfilesForLocation(location);
        }

        foreach (var stalePath in _profilesBySettingsPath.Keys.Where(path => !activePaths.Contains(path)).ToArray())
        {
            _profilesBySettingsPath.Remove(stalePath);
            _writeTimes.Remove(stalePath);
            sawChanges = true;
        }

        if (!sawChanges)
        {
            return;
        }

        RebuildMergedCache();
    }

    private static WtProfileInfo[] ReadProfilesForLocation(TerminalSettingsLocation location)
    {
        TestParseCount++;
        if (IsActiveTestSettingsPath(location.SettingsPath))
        {
            TestOnParseForTests?.Invoke();
        }

        return TryReadProfiles(location).ToArray();
    }

    private static bool IsActiveTestSettingsPath(string settingsPath) =>
        TestLocationsOverride is { Length: > 0 } locations
        && locations.Any(entry =>
            string.Equals(entry.SettingsPath, settingsPath, StringComparison.OrdinalIgnoreCase));

    private static void RebuildMergedCache()
    {
        var merged = _profilesBySettingsPath.Values.SelectMany(profiles => profiles).ToList();
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
            profiles = ReadProfilesFromJson(stream, location);
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

    internal static WtProfileInfo[] ReadProfilesFromJson(Stream stream, TerminalSettingsLocation location)
    {
        using var doc = JsonDocument.Parse(stream);

        var defaultGuid = ReadDefaultProfileGuid(doc.RootElement);
        if (!doc.RootElement.TryGetProperty("profiles", out var profilesNode))
        {
            return [];
        }

        var listNode = profilesNode.TryGetProperty("list", out var directList)
            ? directList
            : profilesNode;

        if (listNode.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return listNode
            .EnumerateArray()
            .Select(element => ToProfile(element, defaultGuid, location))
            .Where(p => p is not null)
            .Cast<WtProfileInfo>()
            .ToArray();
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
