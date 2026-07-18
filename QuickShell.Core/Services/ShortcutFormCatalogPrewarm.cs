using System;
using System.Linq;

namespace QuickShell.Services;

/// <summary>
/// Warms the workspace create/edit form catalogs: companion app discovery and the
/// default 1x1 shortcut form template. Separated from terminal warming so the
/// expensive companion probes (vswhere, JetBrains Toolbox, PATH) can run later.
/// </summary>
internal static class ShortcutFormCatalogPrewarm
{
    public static void Warm(string? terminalApplicationId)
    {
        var appId = string.IsNullOrWhiteSpace(terminalApplicationId)
            ? TerminalHostIds.WindowsTerminal
            : terminalApplicationId;

        // Companion dropdown + install probes (vswhere / JetBrains / PATH).
        var companionChoicesJson = CompanionAppCatalog.BuildFormChoicesJson();

        // Terminal launch targets for create/edit form (active app + WT profiles).
        TerminalCatalogPrewarm.Warm(appId);

        var terminalChoicesJson = TerminalCatalog.BuildFormChoicesJson(
            includeDefaultChoice: true,
            appId);

        // Match ShortcutForm.RebuildTemplate schema key for the default 1x1 form.
        const string schemaKey = "commands-admin-companions-v10-name-kw-widths-c1-cmd1";
        ShortcutFormTemplateCache.GetOrBuild(
            commandCount: 1,
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
