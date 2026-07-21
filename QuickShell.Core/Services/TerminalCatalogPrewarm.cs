using QuickShell.Abstractions;

namespace QuickShell.Services;

/// <summary>
/// Warms terminal/profile catalogs used by settings and workspace forms.
/// Safe to call repeatedly: results are cached by <see cref="ITerminalCatalog"/>.
/// </summary>
internal sealed class TerminalCatalogPrewarm
{
    private readonly ITerminalCatalog _catalog;

    public TerminalCatalogPrewarm(ITerminalCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public void Warm(string? terminalApplicationId)
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
            _ = _catalog.BuildFormChoicesJson(includeDefaultChoice: true, id);
        }
    }
}
