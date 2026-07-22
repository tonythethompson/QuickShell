using QuickShell.Core.Services;

namespace QuickShell.Services;

internal sealed class LaunchRowDraft
{
    public LaunchRowKind Kind { get; set; } = LaunchRowKind.Command;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Label { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;

    public string TaskType { get; set; } = TaskTypeCatalog.None;

    public string LaunchTarget { get; set; } = "default";

    public bool RunAsAdmin { get; set; }

    public bool IsEnabled { get; set; } = true;

    public bool IsEditorPlaceholder { get; set; }

    public LaunchRowDraft Clone() =>
        new()
        {
            Kind = Kind,
            Id = Id,
            Label = Label,
            Command = Command,
            TaskType = TaskType,
            LaunchTarget = LaunchTarget,
            RunAsAdmin = RunAsAdmin,
            IsEnabled = IsEnabled,
            IsEditorPlaceholder = IsEditorPlaceholder,
        };
}
