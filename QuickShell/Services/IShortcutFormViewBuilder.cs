using QuickShell.Services.WorkspaceEditor;

namespace QuickShell.Services;

/// <summary>
/// Builds Adaptive Card TemplateJson / DataJson for the in-palette workspace form.
/// </summary>
internal interface IShortcutFormViewBuilder
{
    /// <summary>
    /// Builds the main create/edit form card for the given editor state.
    /// <summary>
/// Builds the primary Adaptive Card for creating or editing a shortcut workspace.
/// </summary>
/// <param name="state">The workspace edit state used to populate the form.</param>
/// <param name="terminalApplicationId">The identifier of the terminal application associated with the workspace.</param>
/// <returns>The template and data JSON for the primary form card.</returns>
    ShortcutFormCard BuildMain(WorkspaceEditState state, string terminalApplicationId);

    /// <summary>
    /// Builds the discard-unsaved-changes confirmation card.
    /// <summary>
/// Builds a card that prompts the user to confirm discarding unsaved changes.
/// </summary>
/// <returns>The discard confirmation card.</returns>
    ShortcutFormCard BuildDiscardPrompt();
}

/// <summary>
/// Adaptive Card template + data JSON pair for a form surface.
/// </summary>
internal readonly record struct ShortcutFormCard(string TemplateJson, string DataJson);
