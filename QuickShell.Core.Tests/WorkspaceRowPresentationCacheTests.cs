using System.Diagnostics;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class WorkspaceRowPresentationCacheTests
{
    private const string Fingerprint = "wt|wt-default";

    private readonly RowPresentationDiagnostics _diagnostics = new();
    private readonly WorkspaceRowPresentationCache _cache;

    public WorkspaceRowPresentationCacheTests()
    {
        _cache = new WorkspaceRowPresentationCache(_diagnostics);
    }

    private static TerminalShortcut CreateShortcut(
        string id = "ws-1",
        string name = "Alpha",
        string? directory = null) =>
        new()
        {
            Id = id,
            Name = name,
            Directory = directory ?? Path.GetTempPath(),
            Command = "echo hi",
        };

    [Fact]
    public void GetOrBuild_SameSnapshot_ReusesPresentationInstance()
    {
        var shortcut = CreateShortcut();

        var first = _cache.GetOrBuild(shortcut, 1, Fingerprint, WorkspaceRowPresentationMode.Home);
        var second = _cache.GetOrBuild(shortcut, 1, Fingerprint, WorkspaceRowPresentationMode.Home);

        Assert.Same(first, second);
        Assert.Equal(1, _diagnostics.GetCount(RowPresentationDiagnostics.CacheBuild));
        Assert.Equal(1, _diagnostics.GetCount(RowPresentationDiagnostics.CacheHit));
    }

    [Fact]
    public void GetOrBuild_HomeAndSearchModes_ShareCacheButKeepDistinctSubtitles()
    {
        var shortcut = CreateShortcut();
        shortcut.LastUsedUtc = DateTime.UtcNow.AddHours(-2);

        var home = _cache.GetOrBuild(shortcut, 1, Fingerprint, WorkspaceRowPresentationMode.Home);
        var search = _cache.GetOrBuild(shortcut, 1, Fingerprint, WorkspaceRowPresentationMode.SearchResult);

        // Same structural facts, page-appropriate subtitles.
        Assert.Equal(home.Title, search.Title);
        Assert.Equal(home.Glyph, search.Glyph);
        Assert.NotEqual(home.Subtitle, search.Subtitle);

        // Both entries live in the shared cache and repeat lookups hit.
        Assert.Same(home, _cache.GetOrBuild(shortcut, 1, Fingerprint, WorkspaceRowPresentationMode.Home));
        Assert.Same(search, _cache.GetOrBuild(shortcut, 1, Fingerprint, WorkspaceRowPresentationMode.SearchResult));
        Assert.Equal(2, _cache.Count);
    }

    [Fact]
    public void GetOrBuild_NewerRepositoryVersion_InvalidatesAndPrunesOldEntries()
    {
        var shortcut = CreateShortcut();

        var v1 = _cache.GetOrBuild(shortcut, 1, Fingerprint, WorkspaceRowPresentationMode.Home);
        var v2 = _cache.GetOrBuild(shortcut, 2, Fingerprint, WorkspaceRowPresentationMode.Home);

        Assert.NotSame(v1, v2);
        Assert.Equal(2, v2.RepositoryVersion);
        // The v1 entry is gone as soon as v2 was observed.
        Assert.Equal(1, _cache.Count);
    }

    [Fact]
    public void GetOrBuild_OlderRepositoryVersion_DoesNotReinsertPrunedEntry()
    {
        var shortcut = CreateShortcut();
        var current = _cache.GetOrBuild(shortcut, 2, Fingerprint, WorkspaceRowPresentationMode.Home);

        var stale = _cache.GetOrBuild(shortcut, 1, Fingerprint, WorkspaceRowPresentationMode.Home);

        Assert.Equal(1, stale.RepositoryVersion);
        Assert.Equal(1, _cache.Count);
        Assert.Same(
            current,
            _cache.GetOrBuild(shortcut, 2, Fingerprint, WorkspaceRowPresentationMode.Home));
    }

    [Fact]
    public void GetOrBuild_SettingsFingerprintChange_RebuildsPresentation()
    {
        var shortcut = CreateShortcut();

        var before = _cache.GetOrBuild(shortcut, 1, "wt|profile-a", WorkspaceRowPresentationMode.Home);
        var after = _cache.GetOrBuild(shortcut, 1, "wt|profile-b", WorkspaceRowPresentationMode.Home);

        Assert.NotSame(before, after);
        Assert.Equal(2, _diagnostics.GetCount(RowPresentationDiagnostics.CacheBuild));
    }

    [Theory]
    [InlineData(@"\\wsl$\NoSuchDistro-QuickShellTest\home\dev")]
    [InlineData(@"\\wsl.localhost\NoSuchDistro-QuickShellTest\home\dev")]
    [InlineData(@"\\no-such-server-quickshell\share\repo")]
    public void GetOrBuild_WslAndUncPaths_DoesNotProbeDirectoryExistence(string directory)
    {
        var shortcut = CreateShortcut(directory: directory);

        // A WSL probe would shell out to wsl.exe and a UNC probe would hit the network
        // with multi-second timeouts. Structural build must be instant and must never
        // classify the row as missing.
        var stopwatch = Stopwatch.StartNew();
        var presentation = _cache.GetOrBuild(shortcut, 1, Fingerprint, WorkspaceRowPresentationMode.Home);
        stopwatch.Stop();

        Assert.DoesNotContain("Folder not found", presentation.Subtitle, StringComparison.Ordinal);
        Assert.True(
            stopwatch.ElapsedMilliseconds < 500,
            $"Row build for '{directory}' took {stopwatch.ElapsedMilliseconds}ms — it probed the path.");
    }

    [Fact]
    public void GetOrBuild_StructurallyInvalidShortcut_IsNeedsRepair()
    {
        var shortcut = CreateShortcut(directory: string.Empty);

        var presentation = _cache.GetOrBuild(shortcut, 1, Fingerprint, WorkspaceRowPresentationMode.SearchResult);

        Assert.Equal(WorkspaceRowState.NeedsRepair, presentation.State);
        Assert.Equal(ShortcutGlyphs.IncidentTriangle, presentation.Glyph);
    }

    [Fact]
    public void GetOrBuild_CacheStaysBounded()
    {
        for (var i = 0; i < WorkspaceRowPresentationCache.MaxEntries + 25; i++)
        {
            // Distinct fingerprints force distinct keys at one version so the bound,
            // not version pruning, is what limits growth.
            _cache.GetOrBuild(CreateShortcut(id: "ws-" + i), 1, "fp-" + i, WorkspaceRowPresentationMode.Home);
        }

        Assert.True(
            _cache.Count <= WorkspaceRowPresentationCache.MaxEntries,
            $"Cache holds {_cache.Count} entries; bound is {WorkspaceRowPresentationCache.MaxEntries}.");
    }

    [Fact]
    public void GetOrBuild_ShortcutWithoutId_BuildsWithoutCaching()
    {
        var shortcut = CreateShortcut(id: "");

        var presentation = _cache.GetOrBuild(shortcut, 1, Fingerprint, WorkspaceRowPresentationMode.Home);

        Assert.Equal("Alpha", presentation.Title);
        Assert.Equal(0, _cache.Count);
    }

    [Fact]
    public void Presentation_DoesNotCaptureMutableShortcutState()
    {
        var shortcut = CreateShortcut(name: "Original");
        var presentation = _cache.GetOrBuild(shortcut, 1, Fingerprint, WorkspaceRowPresentationMode.Home);

        shortcut.Name = "Mutated";
        shortcut.Directory = @"C:\somewhere-else";

        Assert.Equal("Original", presentation.Title);

        // The record must expose only immutable value data — no entity references.
        foreach (var property in typeof(WorkspaceRowPresentation).GetProperties())
        {
            Assert.False(
                typeof(TerminalShortcut).IsAssignableFrom(property.PropertyType),
                $"WorkspaceRowPresentation.{property.Name} leaks a mutable TerminalShortcut.");
            Assert.False(
                typeof(WorkspaceEntry).IsAssignableFrom(property.PropertyType),
                $"WorkspaceRowPresentation.{property.Name} leaks a mutable WorkspaceEntry.");
        }
    }

    [Fact]
    public void Reset_ClearsEntries()
    {
        _cache.GetOrBuild(CreateShortcut(), 5, Fingerprint, WorkspaceRowPresentationMode.Home);
        Assert.Equal(1, _cache.Count);

        _cache.Reset();

        Assert.Equal(0, _cache.Count);
        // After reset an older version is buildable again (no sticky newest-version).
        var rebuilt = _cache.GetOrBuild(CreateShortcut(), 1, Fingerprint, WorkspaceRowPresentationMode.Home);
        Assert.Equal(1, rebuilt.RepositoryVersion);
        Assert.Equal(1, _cache.Count);
    }
}
