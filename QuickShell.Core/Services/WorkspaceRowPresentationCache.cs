using QuickShell.Abstractions;
using QuickShell.Models;
using System.Linq;

namespace QuickShell.Services;

/// <summary>
/// Bounded, version-pruned cache of immutable workspace row presentation data.
/// Building an entry is pure snapshot/string work: no icon extraction, no git process,
/// no directory-existence probes (WSL/UNC/network paths are never touched).
/// Entries for older repository versions are removed as soon as a newer version is seen,
/// so invalidation is revision-driven rather than timer-driven.
/// </summary>
internal sealed class WorkspaceRowPresentationCache : IWorkspaceRowPresentationCache
{
    /// <summary>
    /// One snapshot can produce at most MaxShortcutCount rows per presentation mode.
    /// Two modes plus headroom for a settings-fingerprint change mid-version.
    /// </summary>
    internal const int MaxEntries = ShortcutValidation.MaxShortcutCount * 3;

    private readonly IRowPresentationDiagnostics _diagnostics;
    private readonly ITerminalCatalog _catalog;
    private readonly ITerminalLaunchGlyphs _glyphs;
    private readonly object _sync = new();
    private readonly Dictionary<WorkspaceRowPresentationKey, WorkspaceRowPresentation> _entries = [];
    private long _newestVersion = long.MinValue;

    public WorkspaceRowPresentationCache(
        IRowPresentationDiagnostics? diagnostics = null,
        ITerminalCatalog? catalog = null,
        ITerminalLaunchGlyphs? glyphs = null)
    {
        _diagnostics = diagnostics ?? new RowPresentationDiagnostics();
        var profiles = new WtProfilesService();
        _catalog = catalog ?? new TerminalCatalog(profiles);
        _glyphs = glyphs ?? new TerminalLaunchGlyphs(
            new TerminalProfileResolver(
                new QuickShellSettingsReader(appDataPaths: null, _catalog),
                profiles,
                _catalog));
    }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _entries.Count;
            }
        }
    }

    public WorkspaceRowPresentation GetOrBuild(
        TerminalShortcut shortcut,
        long repositoryVersion,
        string settingsFingerprint,
        WorkspaceRowPresentationMode mode)
    {
        ArgumentNullException.ThrowIfNull(shortcut);

        if (string.IsNullOrWhiteSpace(shortcut.Id))
        {
            // No stable identity — build without caching.
            _diagnostics.Record(RowPresentationDiagnostics.CacheMiss);
            _diagnostics.Record(RowPresentationDiagnostics.CacheBuild);
            return Build(shortcut, repositoryVersion, mode);
        }

        var key = new WorkspaceRowPresentationKey(
            shortcut.Id,
            repositoryVersion,
            settingsFingerprint ?? string.Empty,
            mode);

        lock (_sync)
        {
            if (repositoryVersion > _newestVersion)
            {
                _newestVersion = repositoryVersion;
                PruneOlderVersionsLocked(repositoryVersion);
            }

            if (_entries.TryGetValue(key, out var cached))
            {
                _diagnostics.Record(RowPresentationDiagnostics.CacheHit);
                return cached;
            }
        }

        _diagnostics.Record(RowPresentationDiagnostics.CacheMiss);
        var built = Build(shortcut, repositoryVersion, mode);
        _diagnostics.Record(RowPresentationDiagnostics.CacheBuild);

        lock (_sync)
        {
            if (repositoryVersion < _newestVersion)
            {
                // A newer snapshot won the race while this entry was being built.
                // Return the immutable value to the caller without reviving stale state.
                return built;
            }

            if (repositoryVersion > _newestVersion)
            {
                _newestVersion = repositoryVersion;
                PruneOlderVersionsLocked(repositoryVersion);
            }

            if (_entries.TryGetValue(key, out var cached))
            {
                return cached;
            }

            if (_entries.Count >= MaxEntries)
            {
                // Overflow means fingerprint churn beyond a snapshot's worth of rows;
                // clearing keeps the bound hard and the next refresh rebuilds cheaply.
                _entries.Clear();
            }

            _entries[key] = built;
        }

        return built;
    }

    public void Reset()
    {
        lock (_sync)
        {
            _entries.Clear();
            _newestVersion = long.MinValue;
        }
    }

    private void PruneOlderVersionsLocked(long newestVersion)
    {
        var stale = _entries.Keys
            .Where(key => key.RepositoryVersion < newestVersion)
            .ToList();

        if (stale.Count == 0)
        {
            return;
        }

        foreach (var key in stale)
        {
            _entries.Remove(key);
        }
    }

    private WorkspaceRowPresentation Build(
        TerminalShortcut shortcut,
        long repositoryVersion,
        WorkspaceRowPresentationMode mode)
    {
        // requireDirectoryExists: false — structural repair state only. Directory
        // existence (local, WSL, UNC) is deferred to the launch health check.
        var needsRepair = ShortcutHealth.WouldNeedRepair(shortcut, requireDirectoryExists: false);
        var state = needsRepair
            ? WorkspaceRowState.NeedsRepair
            : shortcut.RunAsAdmin
                ? WorkspaceRowState.AdminLaunch
                : WorkspaceRowState.Healthy;

        // GetListGlyph resolves from task-type catalog and terminal id strings only;
        // Windows Terminal profile icon probing stays on the deferred enrichment path.
        var glyph = ShortcutHealth.GetListGlyph(shortcut, _glyphs, needsRepair);

        var subtitle = mode == WorkspaceRowPresentationMode.SearchResult && !needsRepair
            ? ShortcutDisplay.BuildDirectorySubtitle(shortcut, _catalog)
            : ShortcutHealth.BuildListSubtitle(shortcut, _catalog, requireDirectoryExists: false);

        // Tags derived from volatile status (git attention, running processes) are
        // intentionally NOT cached here — they have no repository revision. Hosts
        // overlay them live at row materialization.
        return new WorkspaceRowPresentation(
            shortcut.Id ?? string.Empty,
            repositoryVersion,
            shortcut.Name,
            subtitle,
            glyph,
            [],
            state);
    }
}
