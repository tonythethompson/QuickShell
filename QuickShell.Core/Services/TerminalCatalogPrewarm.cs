namespace QuickShell.Services;

/// <summary>
/// Warms terminal/profile catalogs used by settings and workspace forms.
/// Safe to call repeatedly: results are cached by <see cref="TerminalCatalog"/>.
/// </summary>
internal static class TerminalCatalogPrewarm
{
    public static void Warm(string? terminalApplicationId)
    {
        var appId = string.IsNullOrWhiteSpace(terminalApplicationId)
            ? TerminalHostIds.WindowsTerminal
            : terminalApplicationId;

        foreach (var id in new[]
                 {
                     appId,
                     TerminalHostIds.WindowsTerminal,
                     TerminalHostIds.IntelligentTerminal,
                 }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _ = TerminalCatalog.BuildFormChoicesJson(includeDefaultChoice: true, id);
        }
    }
}
