namespace QuickShell.Services;

/// <summary>
/// Segoe Fluent UI (MDL2) glyphs for Adaptive Card submit buttons.
/// </summary>
internal static class FormActionGlyphs
{
    public const string Refresh = "\uE72C";

    public const string Save = "\uE74E";

    /// <summary>
    /// Visible button labels for CmdPal forms. Segoe MDL2 private-use glyphs (e.g. E72C)
    /// render as missing boxes in Adaptive Card Action.Submit titles — do not use them there.
    /// </summary>
    public const string RefreshLabel = "Refresh";

    /// <summary>
    /// Compact refresh title for Adaptive Card actions: Unicode ↻ (not MDL2 E72C).
    /// </summary>
    public const string RefreshActionTitle = "\u21BB";

    public const string SaveLabel = "Save";

    public const string BrowseLabel = "Browse";

    public const string PasteLabel = "Paste";

    public const string AddLabel = "Add";

    public const string RemoveLabel = "Remove";

    public const string Add = "\uE710";

    public const string Remove = "\uE74D";

    public const string FolderOpen = "\uE838";

    public const string Paste = "\uE77F";

    public const string BrowseFolderTooltip = "Browse Folder";

    public const string PastePathTooltip = "Paste path";

    public const string RefreshProfileListTooltip = "Refresh profile list";

    public const string RefreshProfileListDetailTooltip =
        "Reload after installing a shell or editing Windows Terminal settings.";

    public const string SaveTerminalDefaultsTooltip = "Save terminal defaults";

    public const string TerminalDefaultsSectionTooltip =
        "Default host and profile for workspaces set to Default. Use Save & close at the bottom to apply.";

    public const string AddCommandTooltip = "Add command";

    public const string AddTaskTypeCommandTooltip = "Add suggested command";

    public const string ClearCommandTooltip = "Clear command";

    public const string TerminalProfileTooltip = "Terminal profile";
}
