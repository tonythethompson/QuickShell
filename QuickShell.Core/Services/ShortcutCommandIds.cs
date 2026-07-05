using System.Text;

namespace QuickShell.Services;

internal static class ShortcutCommandIds
{
    public const string CreateShortcut = "com.quickshell.shortcut-form.create";

    private const string DiscoverCreatePrefix = "com.quickshell.discover.create.";

    private const string OpenPrefix = "com.quickshell.shortcut.open.";
    private const string LaunchSeparator = ".launch.";

    public static string Open(string shortcutId) =>
        OpenPrefix + shortcutId;

    public static string OpenLaunch(string shortcutId, string launchId) =>
        $"{Open(shortcutId)}{LaunchSeparator}{launchId}";

    public static string FavoriteToggle(string shortcutName) =>
        $"com.quickshell.shortcut.favorite.{EncodeNameKey(shortcutName)}";

    public static string DiscoverCreate(string directory) =>
        DiscoverCreatePrefix + EncodeDirectoryKey(directory);

    public static bool TryParseDiscoverCreate(string commandId, out string directoryKey)
    {
        directoryKey = string.Empty;

        if (!commandId.StartsWith(DiscoverCreatePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        directoryKey = commandId[DiscoverCreatePrefix.Length..];
        return TryDecodeHexUtf8(directoryKey, out _);
    }

    public static bool TryDecodeDiscoverCreateDirectory(string commandId, out string directory)
    {
        directory = string.Empty;

        if (!TryParseDiscoverCreate(commandId, out var directoryKey))
        {
            return false;
        }

        return TryDecodeHexUtf8(directoryKey, out directory);
    }

    private static string EncodeDirectoryKey(string directory)
    {
        var normalized = directory.Trim().TrimEnd('\\', '/');
        try
        {
            normalized = Path.GetFullPath(normalized);
        }
        catch
        {
            // Keep trimmed input when full path normalization fails.
        }

        return EncodeNameKey(normalized);
    }

    private static string EncodeNameKey(string name) =>
        Convert.ToHexString(Encoding.UTF8.GetBytes(name)).ToLowerInvariant();

    public static bool TryParseOpen(string commandId, out string key)
    {
        key = string.Empty;

        if (!commandId.StartsWith(OpenPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        key = commandId[OpenPrefix.Length..];
        if (key.EndsWith(".admin", StringComparison.Ordinal))
        {
            key = key[..^".admin".Length];
        }
        else if (key.EndsWith(".standard", StringComparison.Ordinal))
        {
            key = key[..^".standard".Length];
        }

        return !string.IsNullOrWhiteSpace(key);
    }

    public static bool TryParseOpenLaunch(string commandId, out string shortcutId, out string launchId)
    {
        shortcutId = string.Empty;
        launchId = string.Empty;

        if (!commandId.StartsWith(OpenPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var value = commandId[OpenPrefix.Length..];
        var launchSeparatorIndex = value.IndexOf(LaunchSeparator, StringComparison.Ordinal);
        if (launchSeparatorIndex <= 0)
        {
            return false;
        }

        shortcutId = value[..launchSeparatorIndex];
        launchId = value[(launchSeparatorIndex + LaunchSeparator.Length)..];
        if (launchId.EndsWith(".admin", StringComparison.Ordinal))
        {
            launchId = launchId[..^".admin".Length];
        }
        else if (launchId.EndsWith(".standard", StringComparison.Ordinal))
        {
            launchId = launchId[..^".standard".Length];
        }

        return IsStableShortcutId(shortcutId) && IsStableShortcutId(launchId);
    }

    public static bool TryDecodeLegacyNameKey(string key, out string shortcutName)
    {
        shortcutName = string.Empty;

        if (string.IsNullOrWhiteSpace(key) || IsStableShortcutId(key))
        {
            return false;
        }

        return TryDecodeHexUtf8(key, out shortcutName);
    }

    public static bool IsStableShortcutId(string key) =>
        key.Length == 32 && key.All(static c => Uri.IsHexDigit(c));

    private static bool TryDecodeHexUtf8(string encoded, out string value)
    {
        value = string.Empty;

        try
        {
            value = Encoding.UTF8.GetString(Convert.FromHexString(encoded));
            return !string.IsNullOrEmpty(value);
        }
        catch
        {
            return false;
        }
    }
}
