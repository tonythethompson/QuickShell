namespace QuickShell.Services;

internal sealed record LaunchEditorText(
    string AddCommand,
    string OpenInTerminal,
    string RemoveTooltip,
    string EmptyTitle,
    string EmptyGuidance,
    string ValidationAtLeastOne)
{
    public static LaunchEditorText English { get; } = new(
        "Add command",
        "Open in terminal",
        "Remove launch",
        "No launches yet",
        "Add at least one command or terminal launch.",
        "Add at least one launch.");
}
