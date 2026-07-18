namespace QuickShell.Models;

/// <summary>
/// Repository-owned security metadata. This is intentionally separate from
/// <see cref="TerminalShortcut"/> so portable workspace content cannot carry
/// authority into another store.
/// </summary>
internal sealed record WorkspaceSecurityMetadata
{
    public bool IsTrusted { get; init; } = true;

    public long Revision { get; init; } = 1;

}

internal sealed record StoredWorkspace(
    TerminalShortcut Content,
    WorkspaceSecurityMetadata Security,
    long Revision);
