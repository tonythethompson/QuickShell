using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

/// <summary>
/// Bug condition exploration tests for the home pin context menu.
///
/// These tests assert the EXPECTED (correct) behavior: that BuildForHomePin()
/// includes Favorite, Duplicate, Delete, and multi-launch entries.
///
/// On UNFIXED code, these tests WILL FAIL — this confirms the bug exists.
/// After the fix is applied, these tests WILL PASS — confirming the fix works.
///
/// Validates: Requirements 1.1, 1.2, 1.3, 1.4
/// </summary>
public sealed class ShortcutContextCommandsHomePinBugTests : IDisposable
{
    private readonly string _configDirectory;
    private readonly ShortcutRepository _repository;
    private readonly ShortcutDraftStore _drafts;
    private readonly QuickShellSettingsManager _settings;
    public ShortcutContextCommandsHomePinBugTests()
    {
        LaunchExecutorTestEnvironment.Apply();

        _configDirectory = Path.Combine(
            Path.GetTempPath(),
            "quickshell-homepin-bug-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configDirectory);

        _repository = new ShortcutRepository(_configDirectory);
        _drafts = new ShortcutDraftStore(_repository);
        _settings = new QuickShellSettingsManager();

        QuickShellServices.Bind(new QuickShellServices(_repository, _drafts, _settings));
    }

    public void Dispose()
    {
        QuickShellServices.Unbind();
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

    /// <summary>
    /// BuildForHomePin() with a single-launch shortcut should contain
    /// Favorite, Duplicate, and Delete commands.
    ///
    /// On unfixed code: FAILS (confirms bug — these commands are missing).
    /// After fix: PASSES (confirms expected behavior).
    ///
    /// **Validates: Requirements 1.1, 1.2, 1.3**
    /// </summary>
    [Fact]
    public void BuildForHomePin_SingleLaunch_ContainsFavoriteDuplicateDelete()
    {
        var shortcut = CreateHealthyShortcut(isPinned: false, launchCount: 1);
        _repository.Upsert(shortcut);

        var items = ShortcutContextCommands.BuildForHomePin(
            shortcut,
            OnChanged,
            _settings,
            needsRepair: false);

        var titles = items.Select(i => i.Title).ToList();

        // These assertions will FAIL on unfixed code (proving the bug exists):
        Assert.Contains(titles, t =>
            t.Equals(Strings.Command_Favorite_Name, StringComparison.Ordinal)
            || t.Equals(Strings.Command_Unfavorite_Name, StringComparison.Ordinal));

        Assert.Contains(titles, t =>
            t.Equals(Strings.Command_Duplicate_Name, StringComparison.Ordinal));

        Assert.Contains(titles, t =>
            t.Equals(Strings.Command_Delete_Name, StringComparison.Ordinal));
    }

    /// <summary>
    /// BuildForHomePin() with a multi-launch shortcut (2+ enabled launches) should
    /// contain individual launch entries for each enabled command.
    ///
    /// On unfixed code: FAILS (confirms bug — launch entries are missing).
    /// After fix: PASSES (confirms expected behavior).
    ///
    /// **Validates: Requirements 1.4**
    /// </summary>
    [Fact]
    public void BuildForHomePin_MultiLaunch_ContainsIndividualLaunchEntries()
    {
        var shortcut = CreateHealthyShortcut(isPinned: true, launchCount: 3);
        _repository.Upsert(shortcut);

        var items = ShortcutContextCommands.BuildForHomePin(
            shortcut,
            OnChanged,
            _settings,
            needsRepair: false);

        var titles = items.Select(i => i.Title).ToList();

        // Multi-launch shortcuts should show individual launch entries.
        // The launch titles come from ShortcutDisplay.GetLaunchContextMenuTitle
        // which uses the command text (or label for empty commands).
        Assert.Contains(titles, t => t.Contains("npm run dev", StringComparison.Ordinal));
        Assert.Contains(titles, t => t.Contains("dotnet build", StringComparison.Ordinal));
        Assert.Contains(titles, t => t.Contains("claude", StringComparison.Ordinal));
    }

    /// <summary>
    /// BuildForHomePin() with IsPinned=true should contain a toggle favorite command
    /// (text should be "Unfavorite" since the shortcut is already pinned).
    ///
    /// On unfixed code: FAILS (confirms bug — Favorite/Unfavorite command is missing).
    /// After fix: PASSES (confirms expected behavior).
    ///
    /// **Validates: Requirements 1.1**
    /// </summary>
    [Fact]
    public void BuildForHomePin_IsPinned_ContainsToggleFavoriteCommand()
    {
        var shortcut = CreateHealthyShortcut(isPinned: true, launchCount: 1);
        _repository.Upsert(shortcut);

        var items = ShortcutContextCommands.BuildForHomePin(
            shortcut,
            OnChanged,
            _settings,
            needsRepair: false);

        var titles = items.Select(i => i.Title).ToList();

        // When IsPinned=true, the command text should be "Unfavorite"
        Assert.Contains(titles, t =>
            t.Equals(Strings.Command_Unfavorite_Name, StringComparison.Ordinal));
    }

    /// <summary>
    /// BuildForHomePin() with any shortcut should contain a Delete command
    /// that is marked as IsCritical=true.
    ///
    /// On unfixed code: FAILS (confirms bug — Delete command is entirely missing).
    /// After fix: PASSES (confirms expected behavior).
    ///
    /// **Validates: Requirements 1.3**
    /// </summary>
    [Fact]
    public void BuildForHomePin_Delete_IsMarkedCritical()
    {
        var shortcut = CreateHealthyShortcut(isPinned: false, launchCount: 1);
        _repository.Upsert(shortcut);

        var items = ShortcutContextCommands.BuildForHomePin(
            shortcut,
            OnChanged,
            _settings,
            needsRepair: false);

        var deleteItem = items.FirstOrDefault(i =>
            i.Title.Equals(Strings.Command_Delete_Name, StringComparison.Ordinal));

        // On unfixed code, deleteItem will be null (command is missing entirely).
        Assert.NotNull(deleteItem);
        Assert.True(deleteItem.IsCritical, "Delete command should be marked as critical.");
    }

    private static void OnChanged() { }

    private static TerminalShortcut CreateHealthyShortcut(bool isPinned, int launchCount)
    {
        // Use the current working directory so ShortcutHealth.WouldNeedRepair
        // doesn't trigger on directory existence checks (even though we pass needsRepair: false).
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
}
