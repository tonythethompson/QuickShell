namespace QuickShell.Services;

using System.Globalization;

internal static class QuickShellRecentSettings
{
    public const string SettingKey = "recentWorkspaceCount";
    public const int DefaultCount = 8;
    public const int MinCount = 0;
    public const int MaxCount = 100;

    public static int NormalizeCount(int? value) =>
        value switch
        {
            null => DefaultCount,
            < MinCount => MinCount,
            > MaxCount => MaxCount,
            _ => value.Value,
        };

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
