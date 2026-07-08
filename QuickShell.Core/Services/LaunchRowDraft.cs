namespace QuickShell.Services;

internal sealed class LaunchRowDraft
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Command { get; set; } = string.Empty;

    public string TaskType { get; set; } = TaskTypeCatalog.None;

    public string LaunchTarget { get; set; } = "default";

    public bool IsEditorPlaceholder { get; set; }

    public LaunchRowDraft Clone() =>
        new()
        {
            Id = Id,
            Command = Command,
            TaskType = TaskType,
            LaunchTarget = LaunchTarget,
            IsEditorPlaceholder = IsEditorPlaceholder,
        };
}
