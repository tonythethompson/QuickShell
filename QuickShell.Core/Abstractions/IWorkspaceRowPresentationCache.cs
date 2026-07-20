using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Abstractions;

/// <summary>
/// Provider-scoped cache of immutable workspace row presentation data, shared by the
/// home page, fallback page, and root-palette results. Cached values are pure data
/// (strings, descriptors, enums) keyed by workspace id, repository version, settings
/// fingerprint, and presentation mode — never live ListItems or commands.
/// </summary>
internal interface IWorkspaceRowPresentationCache
{
    /// <summary>
    /// Returns the cached presentation for the shortcut at the given repository version,
    /// building and caching it on a miss. Building performs no I/O: no icon extraction,
    /// no git processes, and no directory-existence probes (WSL/UNC paths included).
    /// </summary>
    WorkspaceRowPresentation GetOrBuild(
        TerminalShortcut shortcut,
        long repositoryVersion,
        string settingsFingerprint,
        WorkspaceRowPresentationMode mode);

    /// <summary>Current number of cached entries (test/diagnostic hook).</summary>
    int Count { get; }

    /// <summary>Deterministic reset for tests.</summary>
    void Reset();
}
