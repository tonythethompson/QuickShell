using System.Text.Json;

namespace QuickShell.Services;

internal sealed class WtProfileInfo
{
    public required string Name { get; init; }

    public string? Commandline { get; init; }

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

        return new WtProfileInfo
        {
            Name = name.Trim(),
            Commandline = commandline,
            IsDefault = !string.IsNullOrWhiteSpace(defaultGuid)
                && !string.IsNullOrWhiteSpace(guid)
                && defaultGuid.Equals(guid, StringComparison.OrdinalIgnoreCase),
            Source = location.Source,
            HostExecutable = location.HostExecutable,
            IdPrefix = location.IdPrefix,
            SourceLabel = location.DisplayPrefix,
        };
    }
}
