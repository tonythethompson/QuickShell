using System.Text;

namespace QuickShell.Services;

/// <summary>
/// Stable command ID builders for workspace actions. Parsing lives in <see cref="CommandIdParser"/>.
/// </summary>
internal static class ShortcutCommandIds
{
    public const string CreateShortcut = "com.quickshell.shortcut-form.create";

    public static string Open(string shortcutId) =>
        QuickShellDeepLinkIds.OpenPrefix + shortcutId;

    public static string OpenLaunch(string shortcutId, string launchId) =>
        $"{Open(shortcutId)}{QuickShellDeepLinkIds.LaunchSeparator}{launchId}";

    public static string FavoriteToggle(string shortcutName) =>
        $"com.quickshell.shortcut.favorite.{CommandIdEncoding.EncodeNameKey(shortcutName)}";

    public static string DiscoverCreate(string directory) =>
        QuickShellDeepLinkIds.DiscoverCreatePrefix + CommandIdEncoding.EncodeDirectoryKey(directory);

    public static string WorkspaceStatus(string shortcutId) =>
        QuickShellDeepLinkIds.WorkspaceStatusPrefix + shortcutId;

    public static string WorktreeBranchPicker(string shortcutId) =>
        QuickShellDeepLinkIds.WorktreeBranchPickerPrefix + shortcutId;

    public static string WorktreeBranchSelect(string shortcutId, string branch) =>
        $"{QuickShellDeepLinkIds.WorktreeBranchSelectPrefix}{shortcutId}.{branch}";

    public static string WorktreeBranchClear(string shortcutId) =>
        QuickShellDeepLinkIds.WorktreeBranchClearPrefix + shortcutId;

    public static bool TryDecodeLegacyNameKey(string key, out string shortcutName)
    {
        shortcutName = string.Empty;

        if (string.IsNullOrWhiteSpace(key) || IsStableShortcutId(key))
        {
            return false;
        }

        return CommandIdEncoding.TryDecodeHexUtf8(key, out shortcutName);
    }

    public static bool IsStableShortcutId(string key) =>
        key.Length == 32 && key.All(static c => Uri.IsHexDigit(c));
}
