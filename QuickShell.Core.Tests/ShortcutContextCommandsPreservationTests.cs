using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

/// <summary>
/// Preservation property tests for context menu commands.
///
/// These tests verify CURRENT behavior that must remain unchanged after
/// the bugfix to BuildForHomePin(). They should PASS on unfixed code,
/// confirming the baseline behavior to preserve.
///
/// **Validates: Requirements 2.5, 2.6, 3.1, 3.2, 3.3, 3.4, 3.5**
/// </summary>
[Collection(QuickShellServicesIsolation.Name)]
public sealed class ShortcutContextCommandsPreservationTests : IDisposable
{
    private readonly string _configDirectory;
    private readonly ShortcutRepository _repository;
    private readonly ShortcutDraftStore _drafts;
    private readonly QuickShellSettingsManager _settings;
    private readonly QuickShellLifetime _lifetime = new();

    public ShortcutContextCommandsPreservationTests()
    {
        LaunchExecutorTestEnvironment.Apply();

        _configDirectory = Path.Combine(
            Path.GetTempPath(),
            "quickshell-preservation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configDirectory);

        _repository = new ShortcutRepository(_configDirectory);
        _drafts = new ShortcutDraftStore(_repository);
        _settings = new QuickShellSettingsManager();

        QuickShellServices.Bind(new QuickShellServices(_repository, _drafts, _settings, new FakeProjectAnalysisService(), _lifetime));
    }

    public void Dispose()
    {
        QuickShellServices.Unbind();
        _lifetime.Dispose();
        _drafts.Dispose();
        _repository.Dispose();
        LaunchExecutorTestEnvironment.Reset();

        try
        {
            if (Directory.Exists(_configDirectory))
            {
                Directory.Delete(_configDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Property 2a: BuildForHomePin() NEVER contains Undo/Redo commands
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// BuildForHomePin() must never include Undo or Redo commands,
    /// regardless of shortcut configuration. Undo/Redo are page-level
    /// commands provided by BuildUndoRedoCommands().
    ///
    /// **Validates: Requirements 2.5**
    /// </summary>
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public void BuildForHomePin_NeverContainsUndoRedo(bool isPinned, int launchCount)
    {
        var shortcut = CreateHealthyShortcut(isPinned, launchCount);
        _repository.Upsert(shortcut);

        var items = ShortcutContextCommands.BuildForHomePin(
            shortcut,
            OnChanged,
            _settings,
            needsRepair: false);

        var titles = items.Select(i => i.Title).ToList();

        Assert.DoesNotContain(titles, t =>
            t.Equals(Strings.Menu_Undo, StringComparison.Ordinal));
        Assert.DoesNotContain(titles, t =>
            t.Equals(Strings.Menu_Redo, StringComparison.Ordinal));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Property 2b: BuildForHomePin() NEVER contains pinned move commands
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// BuildForHomePin() must never include MoveFavoriteShortcutCommand entries
    /// (move up/down/top/bottom). These are for the QuickShell favorites ordering
    /// on a different page.
    ///
    /// **Validates: Requirements 2.6**
    /// </summary>
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void BuildForHomePin_NeverContainsPinnedMoveCommands(bool isPinned, int launchCount)
    {
        var shortcut = CreateHealthyShortcut(isPinned, launchCount);
        _repository.Upsert(shortcut);

        var items = ShortcutContextCommands.BuildForHomePin(
            shortcut,
            OnChanged,
            _settings,
            needsRepair: false);

        var titles = items.Select(i => i.Title).ToList();

        Assert.DoesNotContain(titles, t =>
            t.Equals(Strings.Command_MoveUp_Name, StringComparison.Ordinal));
        Assert.DoesNotContain(titles, t =>
            t.Equals(Strings.Command_MoveDown_Name, StringComparison.Ordinal));
        Assert.DoesNotContain(titles, t =>
            t.Equals(Strings.Command_MoveToTop_Name, StringComparison.Ordinal));
        Assert.DoesNotContain(titles, t =>
            t.Equals(Strings.Command_MoveToBottom_Name, StringComparison.Ordinal));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Property 2c: BuildForHomePin() CONTINUES to contain elevation,
    //              folder/link, workspace status, and Edit commands
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// BuildForHomePin() must always include elevation toggle (Run as Admin
    /// or Run Normally depending on shortcut.RunAsAdmin).
    ///
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildForHomePin_AlwaysContainsElevationToggle(bool runAsAdmin)
    {
        var shortcut = CreateHealthyShortcut(isPinned: false, launchCount: 1);
        _repository.Upsert(shortcut);
        shortcut.RunAsAdmin = runAsAdmin;

        var items = ShortcutContextCommands.BuildForHomePin(
            shortcut,
            OnChanged,
            _settings,
            needsRepair: false);

        var titles = items.Select(i => i.Title).ToList();

        if (runAsAdmin)
        {
            Assert.Contains(titles, t =>
                t.Equals(Strings.Menu_RunNormally, StringComparison.Ordinal));
        }
        else
        {
            Assert.Contains(titles, t =>
                t.Equals(Strings.Menu_RunAsAdmin, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// BuildForHomePin() must always include "Open in File Explorer" and "Copy path"
    /// folder/link commands.
    ///
    /// **Validates: Requirements 3.2**
    /// </summary>
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(true, 3)]
    public void BuildForHomePin_AlwaysContainsFolderAndLinkCommands(bool isPinned, int launchCount)
    {
        var shortcut = CreateHealthyShortcut(isPinned, launchCount);
        _repository.Upsert(shortcut);

        var items = ShortcutContextCommands.BuildForHomePin(
            shortcut,
            OnChanged,
            _settings,
            needsRepair: false);

        var titles = items.Select(i => i.Title).ToList();

        Assert.Contains(titles, t =>
            t.Equals(Strings.Menu_OpenInFileExplorer, StringComparison.Ordinal));
        Assert.Contains(titles, t =>
            t.Equals(Strings.Menu_CopyPath, StringComparison.Ordinal));
    }

    /// <summary>
    /// BuildForHomePin() must always include "Workspace status..." command.
    ///
    /// **Validates: Requirements 3.3**
    /// </summary>
    [Fact]
    public void BuildForHomePin_AlwaysContainsWorkspaceStatus()
    {
        var shortcut = CreateHealthyShortcut(isPinned: true, launchCount: 1);
        _repository.Upsert(shortcut);

        var items = ShortcutContextCommands.BuildForHomePin(
            shortcut,
            OnChanged,
            _settings,
            needsRepair: false);

        var titles = items.Select(i => i.Title).ToList();

        Assert.Contains(titles, t =>
            t.Contains("Workspace status", StringComparison.Ordinal));
    }

    /// <summary>
    /// BuildForHomePin() must always include the Edit command.
    ///
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void BuildForHomePin_AlwaysContainsEditCommand(bool isPinned, int launchCount)
    {
        var shortcut = CreateHealthyShortcut(isPinned, launchCount);
        _repository.Upsert(shortcut);

        var items = ShortcutContextCommands.BuildForHomePin(
            shortcut,
            OnChanged,
            _settings,
            needsRepair: false);

        var titles = items.Select(i => i.Title).ToList();

        Assert.Contains(titles, t =>
            t.Equals(Strings.Menu_Edit, StringComparison.Ordinal));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Property 2d: Build() produces expected structure (documenting it)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Build() for a non-pinned single-launch shortcut produces elevation,
    /// folder/link, status, diagnostics (if set), edit, favorite, duplicate,
    /// and delete — but NOT multi-launch entries or move commands.
    ///
    /// **Validates: Requirements 3.5**
    /// </summary>
    [Fact]
    public void Build_SingleLaunchNotPinned_ContainsExpectedStructure()
    {
        var shortcut = CreateHealthyShortcut(isPinned: false, launchCount: 1);
        _repository.Upsert(shortcut);

        var items = ShortcutContextCommands.Build(
            shortcut,
            OnChanged,
            _settings);

        var titles = items.Select(i => i.Title).ToList();

        // Elevation toggle
        Assert.Contains(titles, t =>
            t.Equals(Strings.Menu_RunAsAdmin, StringComparison.Ordinal)
            || t.Equals(Strings.Menu_RunNormally, StringComparison.Ordinal));

        // Folder/link commands
        Assert.Contains(titles, t =>
            t.Equals(Strings.Menu_OpenInFileExplorer, StringComparison.Ordinal));
        Assert.Contains(titles, t =>
            t.Equals(Strings.Menu_CopyPath, StringComparison.Ordinal));

        // Status
        Assert.Contains(titles, t =>
            t.Contains("Workspace status", StringComparison.Ordinal));

        // Edit
        Assert.Contains(titles, t =>
            t.Equals(Strings.Menu_Edit, StringComparison.Ordinal));

        // Favorite
        Assert.Contains(titles, t =>
            t.Equals(Strings.Command_Favorite_Name, StringComparison.Ordinal)
            || t.Equals(Strings.Command_Unfavorite_Name, StringComparison.Ordinal));

        // Duplicate
        Assert.Contains(titles, t =>
            t.Equals(Strings.Command_Duplicate_Name, StringComparison.Ordinal));

        // Delete
        Assert.Contains(titles, t =>
            t.Equals(Strings.Command_Delete_Name, StringComparison.Ordinal));

        // NOT pinned move commands (not pinned shortcut)
        Assert.DoesNotContain(titles, t =>
            t.Equals(Strings.Command_MoveUp_Name, StringComparison.Ordinal));
        Assert.DoesNotContain(titles, t =>
            t.Equals(Strings.Command_MoveDown_Name, StringComparison.Ordinal));
    }

    /// <summary>
    /// Build() for a multi-launch shortcut contains individual launch entries
    /// at the top of the menu.
    ///
    /// **Validates: Requirements 3.5**
    /// </summary>
    [Fact]
    public void Build_MultiLaunch_ContainsLaunchEntries()
    {
        var shortcut = CreateHealthyShortcut(isPinned: false, launchCount: 3);
        _repository.Upsert(shortcut);

        var items = ShortcutContextCommands.Build(
            shortcut,
            OnChanged,
            _settings);

        var titles = items.Select(i => i.Title).ToList();

        // Multi-launch entries appear (3 launches)
        Assert.Contains(titles, t => t.Contains("npm run dev", StringComparison.Ordinal));
        Assert.Contains(titles, t => t.Contains("dotnet build", StringComparison.Ordinal));
        Assert.Contains(titles, t => t.Contains("claude", StringComparison.Ordinal));
    }

    /// <summary>
    /// Build() never contains Undo/Redo commands — those are provided
    /// separately by BuildUndoRedoCommands().
    ///
    /// **Validates: Requirements 3.5**
    /// </summary>
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    public void Build_NeverContainsUndoRedo(bool isPinned, int launchCount)
    {
        var shortcut = CreateHealthyShortcut(isPinned, launchCount);
        _repository.Upsert(shortcut);

        var items = ShortcutContextCommands.Build(
            shortcut,
            OnChanged,
            _settings);

        var titles = items.Select(i => i.Title).ToList();

        Assert.DoesNotContain(titles, t =>
            t.Equals(Strings.Menu_Undo, StringComparison.Ordinal));
        Assert.DoesNotContain(titles, t =>
            t.Equals(Strings.Menu_Redo, StringComparison.Ordinal));
    }

    /// <summary>
    /// Build() with IsPinned=true and moveVisibility showing all directions
    /// includes move commands — this confirms pinned move commands belong
    /// in Build(), not in BuildForHomePin().
    ///
    /// **Validates: Requirements 3.5**
    /// </summary>
    [Fact]
    public void Build_PinnedWithMoveVisibility_ContainsMoveCommands()
    {
        var shortcut = CreateHealthyShortcut(isPinned: true, launchCount: 1);
        _repository.Upsert(shortcut);

        var moveVisibility = new PinnedMoveVisibility(
            ShowUp: true,
            ShowDown: true,
            ShowToTop: true,
            ShowToBottom: true);

        var items = ShortcutContextCommands.Build(
            shortcut,
            OnChanged,
            _settings,
            moveVisibility: moveVisibility);

        var titles = items.Select(i => i.Title).ToList();

        Assert.Contains(titles, t =>
            t.Equals(Strings.Command_MoveUp_Name, StringComparison.Ordinal));
        Assert.Contains(titles, t =>
            t.Equals(Strings.Command_MoveDown_Name, StringComparison.Ordinal));
        Assert.Contains(titles, t =>
            t.Equals(Strings.Command_MoveToTop_Name, StringComparison.Ordinal));
        Assert.Contains(titles, t =>
            t.Equals(Strings.Command_MoveToBottom_Name, StringComparison.Ordinal));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Property 2e: BuildRepairOnly() produces expected output for
    //              repair-needing shortcuts
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// BuildRepairOnly() with settings produces a minimal menu: status,
    /// diagnostics (conditional), edit, favorite (if pinned), and delete.
    ///
    /// **Validates: Requirements 3.5**
    /// </summary>
    [Fact]
    public void BuildRepairOnly_WithSettings_ContainsMinimalMenu()
    {
        var shortcut = CreateRepairNeededShortcut(isPinned: false);

        var items = ShortcutContextCommands.BuildRepairOnly(
            shortcut,
            OnChanged,
            _settings);

        var titles = items.Select(i => i.Title).ToList();

        // Status
        Assert.Contains(titles, t =>
            t.Contains("Workspace status", StringComparison.Ordinal));

        // Edit
        Assert.Contains(titles, t =>
            t.Equals(Strings.Menu_Edit, StringComparison.Ordinal));

        // Delete
        Assert.Contains(titles, t =>
            t.Equals(Strings.Command_Delete_Name, StringComparison.Ordinal));

        // NOT pinned → no favorite
        Assert.DoesNotContain(titles, t =>
            t.Equals(Strings.Command_Favorite_Name, StringComparison.Ordinal)
            || t.Equals(Strings.Command_Unfavorite_Name, StringComparison.Ordinal));

        // Never elevation, folder/link, or move commands in repair mode
        Assert.DoesNotContain(titles, t =>
            t.Equals(Strings.Menu_RunAsAdmin, StringComparison.Ordinal));
        Assert.DoesNotContain(titles, t =>
            t.Equals(Strings.Menu_OpenInFileExplorer, StringComparison.Ordinal));
    }

    /// <summary>
    /// BuildRepairOnly() for a pinned shortcut includes the Favorite/Unfavorite
    /// toggle so the user can unpin a broken shortcut.
    ///
    /// **Validates: Requirements 3.5**
    /// </summary>
    [Fact]
    public void BuildRepairOnly_Pinned_ContainsFavoriteToggle()
    {
        var shortcut = CreateRepairNeededShortcut(isPinned: true);

        var items = ShortcutContextCommands.BuildRepairOnly(
            shortcut,
            OnChanged,
            _settings);

        var titles = items.Select(i => i.Title).ToList();

        Assert.Contains(titles, t =>
            t.Equals(Strings.Command_Unfavorite_Name, StringComparison.Ordinal));
    }

    /// <summary>
    /// BuildRepairOnly() without settings only has Edit and Delete (no status).
    ///
    /// **Validates: Requirements 3.5**
    /// </summary>
    [Fact]
    public void BuildRepairOnly_WithoutSettings_OnlyEditAndDelete()
    {
        var shortcut = CreateRepairNeededShortcut(isPinned: false);

        var items = ShortcutContextCommands.BuildRepairOnly(
            shortcut,
            OnChanged,
            settings: null);

        var titles = items.Select(i => i.Title).ToList();

        Assert.Contains(titles, t =>
            t.Equals(Strings.Menu_Edit, StringComparison.Ordinal));
        Assert.Contains(titles, t =>
            t.Equals(Strings.Command_Delete_Name, StringComparison.Ordinal));

        // No status without settings
        Assert.DoesNotContain(titles, t =>
            t.Contains("Workspace status", StringComparison.Ordinal));
    }

    /// <summary>
    /// Build() delegates to BuildRepairOnly() when the shortcut needs repair.
    /// The output structure matches BuildRepairOnly() exactly.
    ///
    /// **Validates: Requirements 3.5**
    /// </summary>
    [Fact]
    public void Build_RepairNeeded_DelegatesToBuildRepairOnly()
    {
        var shortcut = CreateRepairNeededShortcut(isPinned: false);

        var buildItems = ShortcutContextCommands.Build(
            shortcut,
            OnChanged,
            _settings);

        var repairItems = ShortcutContextCommands.BuildRepairOnly(
            shortcut,
            OnChanged,
            _settings);

        // Both should produce the same titles in the same order
        var buildTitles = buildItems.Select(i => i.Title).ToList();
        var repairTitles = repairItems.Select(i => i.Title).ToList();

        Assert.Equal(repairTitles.Count, buildTitles.Count);
        for (int i = 0; i < repairTitles.Count; i++)
        {
            Assert.Equal(repairTitles[i], buildTitles[i]);
        }
    }

    /// <summary>
    /// BuildForHomePin() delegates to BuildRepairOnly() when repair is needed.
    ///
    /// **Validates: Requirements 3.5**
    /// </summary>
    [Fact]
    public void BuildForHomePin_RepairNeeded_DelegatesToBuildRepairOnly()
    {
        var shortcut = CreateRepairNeededShortcut(isPinned: false);

        var homePinItems = ShortcutContextCommands.BuildForHomePin(
            shortcut,
            OnChanged,
            _settings,
            needsRepair: true);

        var repairItems = ShortcutContextCommands.BuildRepairOnly(
            shortcut,
            OnChanged,
            _settings);

        var homePinTitles = homePinItems.Select(i => i.Title).ToList();
        var repairTitles = repairItems.Select(i => i.Title).ToList();

        Assert.Equal(repairTitles.Count, homePinTitles.Count);
        for (int i = 0; i < repairTitles.Count; i++)
        {
            Assert.Equal(repairTitles[i], homePinTitles[i]);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private static void OnChanged() { }

    private static TerminalShortcut CreateHealthyShortcut(bool isPinned, int launchCount)
    {
        var directory = Environment.CurrentDirectory;

        var launches = new List<WorkspaceEntry>();
        if (launchCount >= 1)
        {
            launches.Add(new WorkspaceEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Label = "Dev server",
                Terminal = "wt",
                Command = "npm run dev",
                IsEnabled = true,
                Order = 0,
            });
        }

        if (launchCount >= 2)
        {
            launches.Add(new WorkspaceEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Label = "Build",
                Terminal = "wt",
                Command = "dotnet build",
                IsEnabled = true,
                Order = 1,
            });
        }

        if (launchCount >= 3)
        {
            launches.Add(new WorkspaceEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Label = "Claude",
                Terminal = "wt",
                Command = "claude",
                IsEnabled = true,
                Order = 2,
            });
        }

        return new TerminalShortcut
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "TestWorkspace-" + Guid.NewGuid().ToString("N")[..8],
            Directory = directory,
            Terminal = "wt",
            IsPinned = isPinned,
            Launches = launches,
        };
    }

    private static TerminalShortcut CreateRepairNeededShortcut(bool isPinned)
    {
        // Use a non-existent directory to trigger WouldNeedRepair
        return new TerminalShortcut
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "BrokenWorkspace-" + Guid.NewGuid().ToString("N")[..8],
            Directory = @"C:\NonExistent\Path\That\Does\Not\Exist-" + Guid.NewGuid().ToString("N"),
            Terminal = "wt",
            IsPinned = isPinned,
            Launches =
            [
                new WorkspaceEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Label = "Default",
                    Terminal = "wt",
                    Command = "echo hello",
                    IsEnabled = true,
                    Order = 0,
                }
            ],
        };
    }
}
