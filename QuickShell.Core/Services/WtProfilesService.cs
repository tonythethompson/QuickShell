using System.Text.Json;
using QuickShell.Abstractions;

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

internal sealed class WtProfilesService : IWtProfilesService
{
    private readonly object _sync = new();
    private readonly IReadOnlyList<TerminalSettingsLocation>? _fixedLocations;
    private readonly Action? _onParse;

    /// <summary>
    /// When the profile list is warm, skip re-statting every settings path on every call.
    /// Terminal resolution can hit <see cref="GetProfiles"/> many times per form/launch.
    /// </summary>
    private const int RefreshCheckMinIntervalMs = 2000;

    private WtProfileInfo[] _cached = [];
    private readonly Dictionary<string, DateTime> _writeTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WtProfileInfo[]> _profilesBySettingsPath = new(StringComparer.OrdinalIgnoreCase);
    private TerminalSettingsLocation[] _locations = [];
    private long _lastRefreshCheckTickMs;
    private int _parseCount;

    private readonly IReadOnlyList<string>? _fragmentRoots;
    private IReadOnlyDictionary<string, TerminalFragmentProfile> _fragmentProfiles =
        new Dictionary<string, TerminalFragmentProfile>(StringComparer.OrdinalIgnoreCase);
    private string _fragmentFingerprint = string.Empty;

    public WtProfilesService(
        IReadOnlyList<TerminalSettingsLocation>? locations = null,
        Action? onParse = null,
        IEnumerable<string>? fragmentRoots = null)
    {
        _fixedLocations = locations;
        _onParse = onParse;
        _fragmentRoots = fragmentRoots?.ToArray();
    }

    /// <summary>Number of settings files parsed since construction (tests).</summary>
    internal int ParseCount
    {
        get
        {
            lock (_sync)
            {
                return _parseCount;
            }
        }
    }

    public void InvalidateCache()
    {
        lock (_sync)
        {
            _cached = [];
            _writeTimes.Clear();
            _profilesBySettingsPath.Clear();
            _locations = [];
            _lastRefreshCheckTickMs = 0;
            _parseCount = 0;
        }

        WindowsTerminalInstallDiscovery.InvalidateCache();
    }

    private TerminalSettingsLocation[] GetLocations()
    {
        if (_fixedLocations is { Count: > 0 })
        {
            return [.. _fixedLocations];
        }

        if (_locations.Length == 0)
        {
            _locations = [.. TerminalSettingsDiscovery.DiscoverLocations()];
        }

        return _locations;
    }

    public IReadOnlyList<WtProfileInfo> GetProfiles()
    {
        lock (_sync)
        {
            RefreshCacheIfNeeded();
            return _cached;
        }
    }

    public IReadOnlyList<string> GetProfileNames() =>
        GetProfiles().Select(p => p.Name).ToArray();

    public WtProfileInfo? FindProfileForLaunch(string? terminal, string? profileName)
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

    public WtProfileInfo? FindProfileByNameAcrossHosts(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return null;
        }

        var trimmedName = profileName.Trim();
        return GetProfiles().FirstOrDefault(profile =>
            profile.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase));
    }

    public WtProfileInfo? FindDefaultProfile(string hostTerminal)
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

    public WtProfileInfo? FindProfileForStandaloneShell(string shellId)
    {
        var normalized = (shellId ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "powershell7" => "pwsh",
            _ => (shellId ?? string.Empty).Trim().ToLowerInvariant(),
        };

        return GetProfiles().FirstOrDefault(profile => MatchesStandaloneShell(profile, normalized));
    }

    public IReadOnlyList<WtProfileInfo> GetProfilesForApplication(string terminalApplicationId)
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

    private void RefreshCacheIfNeeded()
    {
        var forceRefresh = _cached.Length == 0 && _writeTimes.Count == 0 && _profilesBySettingsPath.Count == 0;
        var nowTick = Environment.TickCount64;

        // Warm cache + non-fixed locations: reuse last merge without re-statting
        // every known settings.json on each terminal-target resolution.
        if (!forceRefresh
            && _cached.Length > 0
            && _fixedLocations is null
            && nowTick - _lastRefreshCheckTickMs < RefreshCheckMinIntervalMs)
        {
            return;
        }

        _lastRefreshCheckTickMs = nowTick;
        var sawChanges = forceRefresh;
        var locations = GetLocations();

        var fragmentFingerprint = TerminalFragmentDiscovery.ComputeFingerprint(_fragmentRoots);
        if (fragmentFingerprint != _fragmentFingerprint)
        {
            _fragmentProfiles = TerminalFragmentDiscovery.LoadAll(_fragmentRoots, out var hadReadFailures);
            // Only commit the fingerprint when every discovered file was readable. Otherwise a
            // transient lock would pin an incomplete profile set until mtime/size/content change.
            if (!hadReadFailures)
            {
                _fragmentFingerprint = fragmentFingerprint;
            }

            _profilesBySettingsPath.Clear();
            _writeTimes.Clear();
            sawChanges = true;
        }

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

    private WtProfileInfo[] ReadProfilesForLocation(TerminalSettingsLocation location)
    {
        _parseCount++;
        if (IsActiveFixedSettingsPath(location.SettingsPath))
        {
            _onParse?.Invoke();
        }

        return TryReadProfiles(location, _fragmentProfiles).ToArray();
    }

    private bool IsActiveFixedSettingsPath(string settingsPath) =>
        _fixedLocations is { Count: > 0 } locations
        && locations.Any(entry =>
            string.Equals(entry.SettingsPath, settingsPath, StringComparison.OrdinalIgnoreCase));

    private void RebuildMergedCache()
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

    private static IEnumerable<WtProfileInfo> TryReadProfiles(
        TerminalSettingsLocation location,
        IReadOnlyDictionary<string, TerminalFragmentProfile>? fragments = null)
    {
        if (!File.Exists(location.SettingsPath))
        {
            yield break;
        }

        WtProfileInfo[] profiles;
        try
        {
            using var stream = File.OpenRead(location.SettingsPath);
            profiles = ReadProfilesFromJson(stream, location, fragments);
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

    internal static WtProfileInfo[] ReadProfilesFromJson(
        Stream stream,
        TerminalSettingsLocation location,
        IReadOnlyDictionary<string, TerminalFragmentProfile>? fragments = null)
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
            .Select(element => ToProfile(element, defaultGuid, location, fragments))
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

    private static WtProfileInfo? ToProfile(
        JsonElement element,
        string? defaultGuid,
        TerminalSettingsLocation location,
        IReadOnlyDictionary<string, TerminalFragmentProfile>? fragments = null)
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

        if (!string.IsNullOrWhiteSpace(guid) && fragments is not null)
        {
            var normalized = NormalizeGuid(guid);
            if (fragments.TryGetValue(normalized, out var fragment))
            {
                if (string.IsNullOrWhiteSpace(commandline) && !string.IsNullOrWhiteSpace(fragment.Commandline))
                {
                    commandline = fragment.Commandline;
                }

                if (string.IsNullOrWhiteSpace(icon) && !string.IsNullOrWhiteSpace(fragment.Icon))
                {
                    icon = fragment.Icon;
                }
            }
        }

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
