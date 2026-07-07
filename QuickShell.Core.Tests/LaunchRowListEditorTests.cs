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
            new(),
            new(),
        };

        var trimmed = LaunchRowListEditor.TrimForSave(rows);

        Assert.Single(trimmed);
        Assert.Equal("npm start", trimmed[0].Command);
    }

    [Fact]
    public void EnsureMinimumRowsForEditor_PadsToThree()
    {
        var rows = new List<LaunchRowDraft> { new() { LaunchTarget = "default" } };

        LaunchRowListEditor.EnsureMinimumRowsForEditor(rows, "default");

        Assert.Equal(3, rows.Count);
    }
}
