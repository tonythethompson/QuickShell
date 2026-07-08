namespace QuickShell.Services;

internal static class QuickShellMultiLaunchSettings
{
    public const string SettingKey = "multiLaunchPresentation";
    public const string SingleWindowTabs = "singleWindowTabs";
    public const string SeparateWindows = "separateWindows";

    public static bool IsSeparateWindows(string? raw) =>
        SeparateWindows.Equals(raw?.Trim(), StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string? raw) =>
        IsSeparateWindows(raw) ? SeparateWindows : SingleWindowTabs;
}
