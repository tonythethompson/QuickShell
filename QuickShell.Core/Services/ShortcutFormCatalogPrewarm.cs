using System;
using System.Linq;
using QuickShell.Abstractions;

namespace QuickShell.Services;

/// <summary>
/// Warms the workspace create/edit form catalogs: companion app discovery and the
/// default 1x1 shortcut form template. Separated from terminal warming so the
/// expensive companion probes (vswhere, JetBrains Toolbox, PATH) can run later.
/// </summary>
internal static class ShortcutFormCatalogPrewarm
{
    /// <summary>
    /// Prewarms the terminal and companion catalogs and caches the default shortcut form template.
    /// </summary>
    /// <param name="terminalApplicationId">The terminal application identifier, or the Windows Terminal identifier when omitted or blank.</param>
    /// <param name="terminalPrewarm">The service used to warm terminal catalog entries.</param>
    /// <param name="catalog">The terminal catalog used to build form choices.</param>
    public static void Warm(
        string? terminalApplicationId,
        TerminalCatalogPrewarm terminalPrewarm,
        ITerminalCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(terminalPrewarm);
        ArgumentNullException.ThrowIfNull(catalog);

        var appId = string.IsNullOrWhiteSpace(terminalApplicationId)
            ? TerminalHostIds.WindowsTerminal
            : terminalApplicationId;

        // Companion dropdown + install probes (vswhere / JetBrains / PATH).
        var companionChoicesJson = CompanionAppCatalog.BuildFormChoicesJson();

        // Terminal launch targets for create/edit form (active app + WT profiles).
        terminalPrewarm.Warm(appId);

        var terminalChoicesJson = catalog.BuildFormChoicesJson(
            includeDefaultChoice: true,
            appId);

        // Match ShortcutForm.RebuildTemplate schema key for the default 1x1 form.
        const string schemaKey = "commands-admin-companions-v10-name-kw-widths-c1-cmd1";
        ShortcutFormTemplateCache.GetOrBuild(
            commandCount: 1,
            companionCount: 1,
            appId,
            companionChoicesJson,
            schemaKey,
            () => ShortcutFormTemplateJson.BuildTemplate(
                terminalChoicesJson,
                companionChoicesJson,
                [("", TaskTypeCatalog.None, "default", false)],
                "Quick Shell",
                companionCount: 1));
    }
}
