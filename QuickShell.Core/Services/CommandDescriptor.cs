namespace QuickShell.Services;

/// <summary>
/// Typed parse result for a CmdPal deep-link command ID.
/// </summary>
/// <param name="Id">Raw command ID from the host.</param>
/// <param name="Kind">Resolved command category.</param>
/// <param name="WorkspaceId">Shortcut/workspace id for open/launch/git kinds.</param>
/// <param name="LaunchId">Launch id for <see cref="CommandKind.OpenLaunch"/>.</param>
/// <param name="Directory">Decoded directory for discover-create.</param>
/// <param name="Branch">Git branch name for worktree select.</param>
internal sealed record CommandDescriptor(
    string Id,
    CommandKind Kind,
    string? WorkspaceId = null,
    string? LaunchId = null,
    string? Directory = null,
    string? Branch = null);
