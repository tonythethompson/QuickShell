namespace QuickShell.Services;

/// <summary>
/// Shared helpers for parsing line-delimited process output without the
/// intermediate array/string allocations of <see cref="string.Split(char[])"/>.
/// </summary>
internal static class LineParsing
{
    public static List<string> SplitTrimmedNonEmptyLines(string input)
    {
        var lines = new List<string>();

        foreach (var line in input.AsSpan().EnumerateLines())
        {
            var trimmed = line.Trim();
            if (!trimmed.IsEmpty)
            {
                lines.Add(trimmed.ToString());
            }
        }

        return lines;
    }
}
