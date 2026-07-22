namespace QuickShell.Core.Services;

internal sealed record LaunchEditorText(
    string AddCommand,
    string AddOpenInTerminal,
    string OpenInTerminal,
    string RemoveTooltip,
    string EmptyTitle,
    string EmptyGuidance,
    string ValidationAtLeastOne,
    string CommandsSectionTooltip,
    string CommandsSectionTitle)
{
    /// <summary>
    /// English defaults for Core hosts (Run, prewarm, tests) that do not inject localized strings.
    /// </summary>
    public static LaunchEditorText EnglishDefaults { get; } = new(
        "Add command",
        "Add terminal",
        "Open in terminal",
        "Remove launch",
        "No launches yet",
        "Add at least one command or terminal launch.",
        "Add at least one launch.",
        "Add a command or open the folder in a terminal.",
        "Commands");
}
