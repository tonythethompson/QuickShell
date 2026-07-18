using System;
using System.Linq;
using QuickShell.Abstractions.Classification;

namespace QuickShell.Services;

/// <summary>
/// Warms the workspace create/edit form catalogs: companion app discovery and the
/// default 1x1 shortcut form template. Separated from terminal warming so the
/// expensive companion probes (vswhere, JetBrains Toolbox, PATH) can run later.
/// </summary>
internal static class ShortcutFormCatalogPrewarm
{
    public static void Warm(
        string? terminalApplicationId,
        IProjectAnalysisService projectAnalysis)
    {
        ArgumentNullException.ThrowIfNull(projectAnalysis);

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

        // Match ShortcutForm.RebuildFromState for the default create state, whose directory
        // is empty until the user chooses one.
        var taskTypeChoicesJson = TaskTypeCatalog.BuildFormChoicesJson(
            projectAnalysis,
            string.Empty);
        ShortcutFormTemplateCache.GetOrBuild(
            commandCount: 1,
            appId,
            companionChoicesJson,
            taskTypeChoicesJson,
            () => ShortcutFormTemplateJson.BuildTemplate(
                terminalChoicesJson,
                companionChoicesJson,
                [("", TaskTypeCatalog.None, "default", false)],
                "Quick Shell",
                companionCount: 1));
    }
}
