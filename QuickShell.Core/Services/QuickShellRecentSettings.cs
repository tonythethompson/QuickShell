namespace QuickShell.Services;

using System.Globalization;

internal static class QuickShellRecentSettings
{
    public const string SettingKey = "recentWorkspaceCount";
    public const int EnabledCount = 8;
    public const int DefaultCount = EnabledCount;
    public const int MinCount = 0;

    public static bool IsEnabled(int count) => NormalizeCount(count) > MinCount;

    public static int FromEnabled(bool enabled) => enabled ? EnabledCount : MinCount;

    public static int NormalizeCount(int? value) =>
        value switch
        {
            null => DefaultCount,
            <= MinCount => MinCount,
            _ => EnabledCount,
        };

    public static int ClampDisplayCount(int maxCount) =>
        maxCount <= MinCount ? MinCount : Math.Min(maxCount, EnabledCount);

    public static string FormatCount(int count) =>
        NormalizeCount(count).ToString(CultureInfo.InvariantCulture);

    public static bool TryParseCount(string? raw, out int count)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            count = DefaultCount;
            return false;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out count)
            || int.TryParse(raw, NumberStyles.Integer, CultureInfo.CurrentCulture, out count))
        {
            count = NormalizeCount(count);
            return true;
        }

        count = DefaultCount;
        return false;
    }
}
