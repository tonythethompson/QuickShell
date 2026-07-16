namespace QuickShell.Services;

/// <summary>
/// Remembers the last successfully launched companion preset (global).
/// Detection reorders peer candidates so a previous choice wins when still eligible.
/// </summary>
internal static class CompanionAppPreference
{
    public const string SettingKey = "lastUsedCompanionPreset";

    /// <summary>Test hook replacing settings I/O.</summary>
    internal static Func<string?>? ReadLastUsedOverride { get; set; }

    /// <summary>Test hook replacing settings I/O.</summary>
    internal static Action<string>? WriteLastUsedOverride { get; set; }

    public static string? ReadLastUsedPreset()
    {
        if (ReadLastUsedOverride is not null)
        {
            return Normalize(ReadLastUsedOverride());
        }

        try
        {
            return Normalize(new QuickShellSettingsReader().ReadRawSetting(SettingKey));
        }
        catch
        {
            return null;
        }
    }

    public static void RememberPreset(string? presetId)
    {
        var normalized = Normalize(presetId);
        if (normalized is null)
        {
            return;
        }

        if (WriteLastUsedOverride is not null)
        {
            WriteLastUsedOverride(normalized);
            return;
        }

        try
        {
            new QuickShellSettingsReader().SaveSetting(SettingKey, normalized);
        }
        catch
        {
            // Best effort.
        }
    }

    public static IReadOnlyList<string> PreferLastUsed(IEnumerable<string> presetIds)
    {
        var ordered = presetIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ordered.Count <= 1)
        {
            return ordered;
        }

        var preferred = ReadLastUsedPreset();
        if (preferred is null)
        {
            return ordered;
        }

        var index = ordered.FindIndex(id => string.Equals(id, preferred, StringComparison.OrdinalIgnoreCase));
        if (index > 0)
        {
            ordered.RemoveAt(index);
            ordered.Insert(0, preferred);
        }

        return ordered;
    }

    private static string? Normalize(string? presetId)
    {
        if (string.IsNullOrWhiteSpace(presetId)
            || string.Equals(presetId, CompanionAppCatalog.PresetNone, StringComparison.OrdinalIgnoreCase)
            || string.Equals(presetId, CompanionAppCatalog.PresetCustom, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return CompanionAppCatalog.IsCatalogPreset(presetId) ? presetId.Trim() : null;
    }
}
