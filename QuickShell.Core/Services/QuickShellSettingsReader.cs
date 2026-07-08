using System.Text.Json;

namespace QuickShell.Services;

internal sealed class QuickShellSettingsReader
{
    private const string TerminalApplicationSettingId = "terminalApplication";
    private const string DefaultProfileSettingId = "defaultProfile";
    private const string BlockDirtyBranchSwitchSettingId = "blockDirtyBranchSwitch";
    private const string MultiLaunchPresentationSettingId = QuickShellMultiLaunchSettings.SettingKey;

    public QuickShellSettingsReader()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuickShell");
        SettingsPath = Path.Combine(directory, "settings.json");
    }

    public string SettingsPath { get; }

    public string TerminalApplicationId => ReadTerminalApplicationId();

    public string DefaultProfileId =>
        ReadDefaultProfileId(TerminalApplicationId);

    public void SaveTerminalDefaults(string terminalApplicationId, string defaultProfileId)
    {
        var app = EnsureValidTerminalApplication(terminalApplicationId);
        var profile = EnsureValidDefaultProfile(app, defaultProfileId);
        WriteSettings(settings =>
        {
            settings[TerminalApplicationSettingId] = app;
            settings[DefaultProfileSettingId] = profile;
        });
        TerminalCatalog.InvalidateCache();
    }

    public bool ReadBlockDirtyBranchSwitch()
    {
        var raw = ReadSetting(BlockDirtyBranchSwitchSettingId);
        return !string.Equals(raw?.Trim(), "false", StringComparison.OrdinalIgnoreCase);
    }

    public bool ReadSeparateWindowsForMultiLaunch() =>
        QuickShellMultiLaunchSettings.IsSeparateWindows(ReadSetting(MultiLaunchPresentationSettingId));

    public void SaveBlockDirtyBranchSwitch(bool enabled) =>
        WriteSettings(settings => settings[BlockDirtyBranchSwitchSettingId] = enabled ? "true" : "false");

    public void SaveMultiLaunchPresentation(bool singleWindowTabs) =>
        WriteSettings(settings =>
            settings[MultiLaunchPresentationSettingId] = singleWindowTabs
                ? QuickShellMultiLaunchSettings.SingleWindowTabs
                : QuickShellMultiLaunchSettings.SeparateWindows);

    public void SaveRecentWorkspaceCount(int count) =>
        WriteSettings(settings =>
            settings[QuickShellRecentSettings.SettingKey] = QuickShellRecentSettings.FormatCount(count));

    public int ReadRecentWorkspaceCount() => ReadRecentWorkspaceCountFromFile(SettingsPath);

    public string ConfigDirectory =>
        Path.GetDirectoryName(SettingsPath)!;

    public string ReadTerminalApplicationId()
    {
        var raw = ReadSetting(TerminalApplicationSettingId);
        if (string.IsNullOrWhiteSpace(raw))
        {
            (raw, _) = LoadLegacyTerminalDefaults();
        }

        return EnsureValidTerminalApplication(raw);
    }

    public string ReadDefaultProfileId(string terminalApplicationId)
    {
        var raw = ReadSetting(DefaultProfileSettingId);
        if (string.IsNullOrWhiteSpace(raw))
        {
            (_, raw) = LoadLegacyTerminalDefaults();
        }

        return EnsureValidDefaultProfile(terminalApplicationId, raw);
    }

    private string? ReadSetting(string key)
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return null;
            }

            using var stream = File.OpenRead(SettingsPath);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.TryGetProperty(key, out var value))
            {
                return value.GetString();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private void WriteSettings(Action<Dictionary<string, string>> mutate)
    {
        var settings = LoadSettingsDictionary();
        mutate(settings);
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        using var stream = File.Create(SettingsPath);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        foreach (var entry in settings.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            writer.WriteString(entry.Key, entry.Value);
        }

        writer.WriteEndObject();
    }

    private Dictionary<string, string> LoadSettingsDictionary()
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return settings;
            }

            using var stream = File.OpenRead(SettingsPath);
            using var document = JsonDocument.Parse(stream);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                settings[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                    JsonValueKind.Number => property.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => property.Value.GetRawText(),
                };
            }
        }
        catch
        {
            return settings;
        }

        return settings;
    }

    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string EnsureValidTerminalApplication(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? TerminalHostIds.LetWindowsChoose
            : value.Trim().ToLowerInvariant();

        if (normalized.Equals(TerminalHostIds.LetWindowsChoose, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalHostIds.LetWindowsChoose;
        }

        if (normalized.Equals(TerminalHostIds.WindowsConsoleHost, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalHostIds.WindowsConsoleHost;
        }

        if (normalized.Equals(TerminalHostIds.IntelligentTerminal, StringComparison.OrdinalIgnoreCase)
            && TerminalCatalog.HasTerminalApplication(TerminalHostIds.IntelligentTerminal))
        {
            return TerminalHostIds.IntelligentTerminal;
        }

        return TerminalHostIds.WindowsTerminal;
    }

    private static string EnsureValidDefaultProfile(string terminalApplicationId, string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? TerminalHostIds.DefaultProfile
            : value.Trim();

        if (normalized.Equals(TerminalHostIds.DefaultProfile, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalHostIds.DefaultProfile;
        }

        if (TerminalCatalog.IsStandaloneShellLaunchTarget(normalized))
        {
            return normalized;
        }

        if (TryExtractProfileName(normalized, out var profileName)
            && TerminalCatalog.GetDefaultProfileIds(terminalApplicationId)
                .Any(id => id.Equals(profileName, StringComparison.OrdinalIgnoreCase)))
        {
            return profileName;
        }

        if (TerminalCatalog.GetDefaultProfileIds(terminalApplicationId)
            .Any(id => id.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return normalized;
        }

        return TerminalHostIds.DefaultProfile;
    }

    private static (string App, string Profile) LoadLegacyTerminalDefaults()
    {
        var legacyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuickShell",
            "settings.json");

        var legacyValue = LoadLegacyDefaultTerminal(legacyPath);
        return MigrateLegacyDefaultTerminal(legacyValue);
    }

    private static (string App, string Profile) MigrateLegacyDefaultTerminal(string legacy)
    {
        var value = TerminalCatalog.NormalizeLaunchTargetId(legacy);

        if (value.Equals(TerminalHostIds.IntelligentTerminal, StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("it:", StringComparison.OrdinalIgnoreCase))
        {
            return (
                TerminalHostIds.IntelligentTerminal,
                value.StartsWith("it:", StringComparison.OrdinalIgnoreCase) ? value[3..] : TerminalHostIds.DefaultProfile);
        }

        if (value.StartsWith("wt:", StringComparison.OrdinalIgnoreCase))
        {
            return (TerminalHostIds.WindowsTerminal, value[3..]);
        }

        if (TerminalCatalog.IsStandaloneShellLaunchTarget(value))
        {
            return (TerminalHostIds.WindowsTerminal, value);
        }

        return (TerminalHostIds.WindowsTerminal, TerminalHostIds.DefaultProfile);
    }

    private static bool TryExtractProfileName(string value, out string profileName)
    {
        if (value.StartsWith("wt:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("it:", StringComparison.OrdinalIgnoreCase))
        {
            profileName = value[(value.IndexOf(':') + 1)..];
            return !string.IsNullOrWhiteSpace(profileName);
        }

        profileName = string.Empty;
        return false;
    }

    private static string LoadLegacyDefaultTerminal(string legacyPath)
    {
        try
        {
            if (!File.Exists(legacyPath))
            {
                return TerminalHostIds.WindowsTerminal;
            }

            using var stream = File.OpenRead(legacyPath);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.TryGetProperty("DefaultTerminal", out var terminal))
            {
                return TerminalCatalog.NormalizeLaunchTargetId(terminal.GetString());
            }

            return TerminalHostIds.WindowsTerminal;
        }
        catch
        {
            return TerminalHostIds.WindowsTerminal;
        }
    }

    internal static int ReadRecentWorkspaceCountFromFile(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
            {
                return QuickShellRecentSettings.DefaultCount;
            }

            using var stream = File.OpenRead(settingsPath);
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty(QuickShellRecentSettings.SettingKey, out var value))
            {
                return QuickShellRecentSettings.DefaultCount;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return QuickShellRecentSettings.NormalizeCount(number);
            }

            if (value.ValueKind == JsonValueKind.String
                && QuickShellRecentSettings.TryParseCount(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }
        catch
        {
            return QuickShellRecentSettings.DefaultCount;
        }

        return QuickShellRecentSettings.DefaultCount;
    }
}
