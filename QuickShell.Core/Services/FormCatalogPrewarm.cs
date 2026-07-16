namespace QuickShell.Services;

/// <summary>
/// Warms workspace-form catalogs (companion dropdown, terminal profiles, default template)
/// so the first Create/Edit open does not pay cold discovery (vswhere, WT settings, etc.).
/// </summary>
internal static class FormCatalogPrewarm
{
    public static void Warm(string? terminalApplicationId = null)
    {
        var appId = string.IsNullOrWhiteSpace(terminalApplicationId)
            ? TerminalHostIds.WindowsTerminal
            : terminalApplicationId;

        // Companion dropdown + install probes (vswhere / JetBrains / PATH).
        var companionChoicesJson = CompanionAppCatalog.BuildFormChoicesJson();

        // Terminal launch targets for create/edit form (active app + WT profiles).
        foreach (var id in new[]
                 {
                     appId,
                     TerminalHostIds.WindowsTerminal,
                     TerminalHostIds.IntelligentTerminal,
                 }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _ = TerminalCatalog.BuildFormChoicesJson(includeDefaultChoice: true, id);
        }

        var terminalChoicesJson = TerminalCatalog.BuildFormChoicesJson(
            includeDefaultChoice: true,
            appId);

        // Match ShortcutForm.RebuildTemplate schema key for the default 1×1 form.
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

