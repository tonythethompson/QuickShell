namespace QuickShell.Services;

internal static class QuickShellBrand
{
    /// <summary>In-app Command Palette name (top-level command, pages, settings).</summary>
    public const string DisplayName = "Quick Shell";

    /// <summary>Localized settings page title, e.g. "Quick Shell settings" in English.</summary>
    public static string SettingsTitle => Strings.SettingsTitleFormat(DisplayName);

    /// <summary>External marketing name (Microsoft Store listing title, website). Not used in CmdPal UI.</summary>
    public const string StoreDisplayName = "Quick Shell for CmdPal";
}
