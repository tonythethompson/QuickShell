using QuickShell.Services.WorkspaceEditor;

namespace QuickShell.Services;

/// <summary>
/// Builds Adaptive Card TemplateJson / DataJson for the in-palette workspace form.
/// </summary>
internal interface IShortcutFormViewBuilder
{
    /// <summary>
    /// Builds the main create/edit form card for the given editor state.
    /// </summary>
    ShortcutFormCard BuildMain(WorkspaceEditState state, string terminalApplicationId);

    /// <summary>
    /// Builds the discard-unsaved-changes confirmation card.
    /// </summary>
    ShortcutFormCard BuildDiscardPrompt();
}

/// <summary>
/// Adaptive Card template + data JSON pair for a form surface.
/// </summary>
internal readonly record struct ShortcutFormCard(string TemplateJson, string DataJson);
