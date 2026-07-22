namespace QuickShell.Services.WorkspaceEditor;

internal readonly record struct WorkspaceEditResult(
    WorkspaceEditResultKind Kind,
    string? Message = null)
{
    public static WorkspaceEditResult StayOpen(string? message = null) =>
        new(WorkspaceEditResultKind.StayOpen, message);

    public static WorkspaceEditResult Saved(string? message = null) =>
        new(WorkspaceEditResultKind.Saved, message);

    public static WorkspaceEditResult Cancelled() =>
        new(WorkspaceEditResultKind.Cancelled);

    public static WorkspaceEditResult Discarded() =>
        new(WorkspaceEditResultKind.Discarded);

    public static WorkspaceEditResult PromptDiscard() =>
        new(WorkspaceEditResultKind.PromptDiscard);
}
