namespace QuickShell.Services;

/// <summary>
/// Structural state of a workspace row, derived only from the repository snapshot.
/// Volatile signals (git status, running processes, launch health) are never part of it.
/// </summary>
internal enum WorkspaceRowState
{
    Healthy,
    AdminLaunch,
    NeedsRepair,
}

/// <summary>
/// How a workspace row is presented. Home rows show the rich launch subtitle;
/// search-result rows (fallback page, root palette) show the directory subtitle.
/// </summary>
internal enum WorkspaceRowPresentationMode
{
    Home,
    SearchResult,
}

/// <summary>
/// A tag rendered on a workspace row, described as pure data so hosts can materialize
/// their own UI tag objects. Only structural (snapshot-derived) tags may be cached;
/// volatile status tags are overlaid at materialization time.
/// </summary>
internal sealed record RowTagDescriptor(
    string Glyph,
    string ToolTip,
    WorkspaceAttentionState Attention);

/// <summary>
/// Immutable presentation data for one workspace row: everything derivable purely from
/// the repository snapshot plus the presentation settings fingerprint. Never holds
/// mutable entities, commands, page callbacks, or host UI objects.
/// </summary>
internal sealed record WorkspaceRowPresentation(
    string WorkspaceId,
    long RepositoryVersion,
    string Title,
    string Subtitle,
    string Glyph,
    IReadOnlyList<RowTagDescriptor> Tags,
    WorkspaceRowState State);

/// <summary>
/// Cache key for <see cref="WorkspaceRowPresentation"/>. A row presentation is valid only
/// for one repository version, one settings fingerprint, and one presentation mode.
/// </summary>
internal readonly record struct WorkspaceRowPresentationKey(
    string WorkspaceId,
    long RepositoryVersion,
    string PresentationSettingsFingerprint,
    WorkspaceRowPresentationMode Mode);
