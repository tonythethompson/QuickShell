using QuickShell.Core.Services;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class ExplicitLaunchRowTests
{
    [Fact]
    public void FromWorkspaceEntries_BlankCommandIsOpenInTerminal()
    {
        var rows = LaunchRowListEditor.FromWorkspaceEntries(
        [
            new WorkspaceEntry { Id = "terminal", Command = null, Order = 0 },
        ]);

        Assert.Equal(LaunchRowKind.OpenInTerminal, Assert.Single(rows).Kind);
    }

    [Fact]
    public void RemoveRow_RemovesWithoutPaddingAndPreservesEffectiveTarget()
    {
        var rows = new List<LaunchRowDraft>
        {
            new() { Command = "one", LaunchTarget = "wt:pwsh" },
            new() { Command = "two", LaunchTarget = TerminalCatalog.SameAsPreviousLaunchTargetId },
            new() { Command = "three", LaunchTarget = TerminalCatalog.SameAsPreviousLaunchTargetId },
        };

        LaunchRowListEditor.RemoveRow(rows, 0, "default");

        Assert.Equal(2, rows.Count);
        Assert.Equal("wt:pwsh", rows[0].LaunchTarget);
        Assert.Equal(TerminalCatalog.SameAsPreviousLaunchTargetId, rows[1].LaunchTarget);
    }

    [Fact]
    public void ApplyPill_AppendsCommandWithoutOverwritingOpenInTerminal()
    {
        var rows = new List<LaunchRowDraft>
        {
            new() { Kind = LaunchRowKind.OpenInTerminal, LaunchTarget = "wt:pwsh" },
        };
        var pill = new CommandSuggestionPill("npm test", TaskTypeCatalog.Test, "Test", "npm test", "npm test", 1, "test");

        Assert.True(LaunchRowListEditor.ApplyPill(rows, pill, "default"));

        Assert.Equal(2, rows.Count);
        Assert.Equal(LaunchRowKind.OpenInTerminal, rows[0].Kind);
        Assert.Equal(LaunchRowKind.Command, rows[1].Kind);
        Assert.Equal("npm test", rows[1].Command);
    }

    [Fact]
    public void ApplyPill_AppendedCommandGetsUniqueInternalLabel()
    {
        var rows = new List<LaunchRowDraft>
        {
            new() { Label = "Command", Command = "one" },
            new() { Label = "Command 2", Command = "two" },
        };
        var pill = new CommandSuggestionPill("three", TaskTypeCatalog.None, "Command", "three", "three", 1, "test");

        LaunchRowListEditor.ApplyPill(rows, pill, "default");

        Assert.Equal("Command 3", rows[2].Label);
    }

    [Fact]
    public void TrimForSave_DropsBlankCommandsAndPreservesOpenInTerminal()
    {
        var rows = new List<LaunchRowDraft>
        {
            new() { Kind = LaunchRowKind.Command },
            new() { Kind = LaunchRowKind.Command, IsEditorPlaceholder = true },
            new() { Kind = LaunchRowKind.OpenInTerminal, LaunchTarget = "wt:pwsh" },
        };

        var trimmed = LaunchRowListEditor.TrimForSave(rows);

        var row = Assert.Single(trimmed);
        Assert.Equal(LaunchRowKind.OpenInTerminal, row.Kind);
    }

    [Fact]
    public void CommandsFromShortcut_NewEditorStartsWithZeroRows()
    {
        Assert.Empty(ShortcutFormLaunchSection.CommandsFromShortcut(null, "default"));
    }

    [Fact]
    public void ToLaunchInputs_PreservesLabelEnabledAndUsesNullCommandForTerminalOnly()
    {
        var inputs = ShortcutFormLaunchSection.ToLaunchInputs(
        [
            new LaunchRowDraft
            {
                Kind = LaunchRowKind.OpenInTerminal,
                Label = "Terminal",
                IsEnabled = false,
            },
        ], "App", "default", "Open in terminal");

        var input = Assert.Single(inputs);
        Assert.Equal("Terminal", input.Label);
        Assert.False(input.IsEnabled);
        Assert.Null(input.Command);
    }
}
