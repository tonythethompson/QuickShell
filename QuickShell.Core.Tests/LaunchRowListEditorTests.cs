using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class LaunchRowListEditorTests
{
    [Fact]
    public void ClearRow_RemovesRowAndCompactsLaterCommands()
    {
        var rows = new List<LaunchRowDraft>
        {
            new() { Command = "npm run dev", TaskType = TaskTypeCatalog.Frontend, LaunchTarget = "default" },
            new() { Command = "npm test", TaskType = TaskTypeCatalog.Test, LaunchTarget = TerminalCatalog.SameAsPreviousLaunchTargetId },
            new() { Command = "docker compose up", TaskType = TaskTypeCatalog.Services, LaunchTarget = TerminalCatalog.SameAsPreviousLaunchTargetId },
        };

        LaunchRowListEditor.ClearRow(rows, 1, "default");

        Assert.Equal(3, rows.Count);
        Assert.Equal("npm run dev", rows[0].Command);
        Assert.Equal("docker compose up", rows[1].Command);
        Assert.Equal(string.Empty, rows[2].Command);
        Assert.True(rows[2].IsEditorPlaceholder);
    }

    [Fact]
    public void ClearRow_FirstRow_ShiftsRemainingCommandsUp()
    {
        var rows = new List<LaunchRowDraft>
        {
            new() { Command = "one", LaunchTarget = "default" },
            new() { Command = "two", LaunchTarget = TerminalCatalog.SameAsPreviousLaunchTargetId },
            new() { Command = "three", LaunchTarget = TerminalCatalog.SameAsPreviousLaunchTargetId },
        };

        LaunchRowListEditor.ClearRow(rows, 0, "default");

        Assert.Equal("two", rows[0].Command);
        Assert.Equal("three", rows[1].Command);
        Assert.Equal(string.Empty, rows[2].Command);
    }

    [Fact]
    public void ClearRow_PreservesEffectiveTargetForSameAsPreviousSuccessor()
    {
        var rows = new List<LaunchRowDraft>
        {
            new() { Command = "one", LaunchTarget = "wt:pwsh" },
            new() { Command = "two", LaunchTarget = TerminalCatalog.SameAsPreviousLaunchTargetId },
            new() { Command = "three", LaunchTarget = TerminalCatalog.SameAsPreviousLaunchTargetId },
        };

        LaunchRowListEditor.ClearRow(rows, 0, "default");

        Assert.Equal("wt:pwsh", rows[0].LaunchTarget);
        Assert.Equal(TerminalCatalog.SameAsPreviousLaunchTargetId, rows[1].LaunchTarget);
    }

    [Fact]
    public void TrimForSave_RemovesAllBlankRowsAndKeepsOne()
    {
        var rows = new List<LaunchRowDraft>
        {
            new() { Command = "npm start" },
            new() { IsEditorPlaceholder = true },
            new() { IsEditorPlaceholder = true },
        };

        var trimmed = LaunchRowListEditor.TrimForSave(rows);

        Assert.Single(trimmed);
        Assert.Equal("npm start", trimmed[0].Command);
    }

    [Fact]
    public void TrimForSave_PreservesIntentionalBlankLaunch()
    {
        var rows = new List<LaunchRowDraft>
        {
            new() { Command = "npm start" },
            new() { LaunchTarget = "wt:pwsh" },
        };

        var trimmed = LaunchRowListEditor.TrimForSave(rows);

        Assert.Equal(2, trimmed.Count);
        Assert.Equal("npm start", trimmed[0].Command);
        Assert.Equal(string.Empty, trimmed[1].Command);
        Assert.Equal("wt:pwsh", trimmed[1].LaunchTarget);
    }

    [Fact]
    public void EnsureMinimumRowsForEditor_PadsToThree()
    {
        var rows = new List<LaunchRowDraft> { new() { LaunchTarget = "default" } };

        LaunchRowListEditor.EnsureMinimumRowsForEditor(rows, "default");

        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void ApplyPill_FillsFirstEmptyCommandSlot()
    {
        var rows = new List<LaunchRowDraft>
        {
            new() { Command = "npm start", LaunchTarget = "default" },
            new() { LaunchTarget = "wt:pwsh", IsEditorPlaceholder = true },
            new() { LaunchTarget = TerminalCatalog.SameAsPreviousLaunchTargetId, IsEditorPlaceholder = true },
        };

        var pill = new CommandSuggestionPill(
            "docker compose up",
            TaskTypeCatalog.Services,
            "Services",
            "docker compose up",
            "Services · docker compose up",
            10,
            "docker");

        Assert.False(LaunchRowListEditor.ApplyPill(rows, pill, "default"));
        Assert.Equal(3, rows.Count);
        Assert.Equal("docker compose up", rows[1].Command);
        Assert.Equal("wt:pwsh", rows[1].LaunchTarget);
        Assert.False(rows[1].IsEditorPlaceholder);
        Assert.Equal(string.Empty, rows[2].Command);
        Assert.True(rows[2].IsEditorPlaceholder);
    }

    [Fact]
    public void ApplyPill_PreservesIntentionalBlankFolderLaunch()
    {
        var rows = new List<LaunchRowDraft>
        {
            new() { Command = "npm start", LaunchTarget = "default" },
            new() { LaunchTarget = "wt:pwsh" },
            new() { LaunchTarget = TerminalCatalog.SameAsPreviousLaunchTargetId, IsEditorPlaceholder = true },
        };

        var pill = new CommandSuggestionPill(
            "docker compose up",
            TaskTypeCatalog.Services,
            "Services",
            "docker compose up",
            "Services · docker compose up",
            10,
            "docker");

        Assert.False(LaunchRowListEditor.ApplyPill(rows, pill, "default"));
        Assert.Equal(string.Empty, rows[1].Command);
        Assert.False(rows[1].IsEditorPlaceholder);
        Assert.Equal("docker compose up", rows[2].Command);
        Assert.False(rows[2].IsEditorPlaceholder);
    }

    [Fact]
    public void ApplyPill_AfterClear_RefillsCompactedEmptySlot()
    {
        var rows = new List<LaunchRowDraft>
        {
            new() { Command = "one", LaunchTarget = "default" },
            new() { Command = "two", LaunchTarget = TerminalCatalog.SameAsPreviousLaunchTargetId },
            new() { Command = "three", LaunchTarget = TerminalCatalog.SameAsPreviousLaunchTargetId },
        };

        LaunchRowListEditor.ClearRow(rows, 1, "default");

        var pill = new CommandSuggestionPill(
            "four",
            TaskTypeCatalog.None,
            "Command",
            "four",
            "four",
            1,
            "custom");

        Assert.False(LaunchRowListEditor.ApplyPill(rows, pill, "default"));
        Assert.Equal(["one", "three", "four"], rows.Select(row => row.Command));
    }
}
