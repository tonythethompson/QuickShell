using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class LaunchRowListEditorTests
{
    [Fact]
    public void ClearRow_ClearsCommandAndTaskType()
    {
        var rows = new List<LaunchRowDraft>
        {
            new() { Command = "npm run dev", TaskType = TaskTypeCatalog.Frontend },
        };

        LaunchRowListEditor.ClearRow(rows, 0);

        Assert.Equal(string.Empty, rows[0].Command);
        Assert.Equal(TaskTypeCatalog.None, rows[0].TaskType);
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
    public void ApplyPill_PrefersEditorPlaceholderOverIntentionalBlank()
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
            "Services · docker compose up",
            "docker compose up",
            10,
            "docker");

        Assert.False(LaunchRowListEditor.ApplyPill(rows, pill, "default"));
        Assert.Equal(3, rows.Count);
        Assert.Equal(string.Empty, rows[1].Command);
        Assert.Equal("wt:pwsh", rows[1].LaunchTarget);
        Assert.Equal("docker compose up", rows[2].Command);
        Assert.False(rows[2].IsEditorPlaceholder);
    }
}
