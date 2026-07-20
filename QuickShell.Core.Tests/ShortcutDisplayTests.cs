using QuickShell.Abstractions;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class ShortcutDisplayTests
{
    private readonly ITerminalCatalog _catalog = new TerminalCatalog(new WtProfilesService());

    [Fact]
    public void GetLaunchContextMenuTitle_UsesCommandText()
    {
        var entry = new WorkspaceEntry
        {
            Id = "a",
            Label = "Trackdub",
            Command = "claude",
            Terminal = "default",
            IsEnabled = true,
            Order = 0,
        };

        Assert.Equal("claude", ShortcutDisplay.GetLaunchContextMenuTitle(entry));
    }

    [Fact]
    public void GetLaunchContextMenuTitle_CollapsesMultilineCommandToSingleLine()
    {
        var entry = new WorkspaceEntry
        {
            Id = "a",
            Label = "Command 2",
            Command = "npm run\r\ndev",
            Terminal = "default",
            IsEnabled = true,
            Order = 1,
        };

        Assert.Equal("npm run dev", ShortcutDisplay.GetLaunchContextMenuTitle(entry));
    }

    [Fact]
    public void GetLaunchContextMenuTitle_FallsBackToLabelWhenCommandBlank()
    {
        var entry = new WorkspaceEntry
        {
            Id = "a",
            Label = "Frontend",
            Command = string.Empty,
            Terminal = "default",
            IsEnabled = true,
            Order = 0,
        };

        Assert.Equal("Frontend", ShortcutDisplay.GetLaunchContextMenuTitle(entry));
    }

    [Fact]
    public void GetLaunchContextMenuTitle_UsesOpenFolderOnlyWhenSiblingHasCommand()
    {
        var withCommand = new WorkspaceEntry
        {
            Id = "a",
            Label = "Dev",
            Command = "npm run dev",
            IsEnabled = true,
            Order = 0,
        };
        var folderOnly = new WorkspaceEntry
        {
            Id = "b",
            Label = "Shell",
            Command = string.Empty,
            IsEnabled = true,
            Order = 1,
        };
        var siblings = new[] { withCommand, folderOnly };

        Assert.Equal("Open folder only", ShortcutDisplay.GetLaunchContextMenuTitle(folderOnly, siblings));
        Assert.Equal("npm run dev", ShortcutDisplay.GetLaunchContextMenuTitle(withCommand, siblings));
    }

    [Fact]
    public void GetLaunchContextMenuTitle_UsesOpenFolderWhenCommandAndLabelBlank()
    {
        var entry = new WorkspaceEntry
        {
            Id = "a",
            Label = string.Empty,
            Command = string.Empty,
            Terminal = "default",
            IsEnabled = true,
            Order = 0,
        };

        Assert.Equal("Open folder", ShortcutDisplay.GetLaunchContextMenuTitle(entry));
    }

    [Fact]
    public void CopyPath_UsesCopyGlyph_NotCopyTo()
    {
        Assert.Equal("\uE8C8", ShortcutGlyphs.CopyPath);
        Assert.NotEqual("\uF413", ShortcutGlyphs.CopyPath);
        Assert.NotEqual("\uE77E", ShortcutGlyphs.CopyPath);
    }

    [Fact]
    public void Duplicate_UsesCopyToGlyph()
    {
        Assert.Equal("\uF413", ShortcutGlyphs.Duplicate);
        Assert.NotEqual("\uE8C8", ShortcutGlyphs.Duplicate);
    }

    [Fact]
    public void BuildSubtitle_LeavesCompanionAppDetailOutOfRowSummary()
    {
        var shortcut = new TerminalShortcut
        {
            Name = "Sample",
            Directory = @"C:\Projects\Sample",
            Terminal = "default",
            CompanionAppPath = @"C:\Apps\Discord\Discord.exe",
            Launches = [new WorkspaceEntry { Label = "Main", IsEnabled = true }],
        };

        var subtitle = ShortcutDisplay.BuildSubtitle(shortcut, _catalog);
        Assert.DoesNotContain("Discord", subtitle, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Main", subtitle, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSubtitle_UsesProfileNameWithoutTerminalHost()
    {
        var shortcut = new TerminalShortcut
        {
            Name = "frontend Copy",
            Directory = @"A:\Trackdub\frontend",
            Launches =
            [
                new WorkspaceEntry
                {
                    Label = "Main",
                    Terminal = "it",
                    WtProfile = "Nushell",
                    Command = "npm run dev",
                    IsEnabled = true,
                },
            ],
        };

        var subtitle = ShortcutDisplay.BuildSubtitle(shortcut, _catalog);

        Assert.Contains("Nushell", subtitle, StringComparison.Ordinal);
        Assert.DoesNotContain("Intelligent Terminal", subtitle, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSubtitle_StandalonePwsh_ShowsPowerShell7()
    {
        var shortcut = new TerminalShortcut
        {
            Name = "frontend",
            Directory = @"A:\Trackdub\frontend",
            Launches =
            [
                new WorkspaceEntry
                {
                    Label = "Main",
                    Terminal = "pwsh",
                    Command = "npm run dev",
                    IsEnabled = true,
                },
            ],
        };

        var subtitle = ShortcutDisplay.BuildSubtitle(shortcut, _catalog);

        Assert.Contains("PowerShell 7", subtitle, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSubtitle_MultipleLaunchesUsesCompactCount()
    {
        var shortcut = new TerminalShortcut
        {
            Name = "frontend",
            Directory = @"A:\Trackdub\frontend",
            Launches =
            [
                new WorkspaceEntry
                {
                    Label = "Dev server",
                    Terminal = "pwsh",
                    Command = "npm run dev",
                    IsEnabled = true,
                    Order = 0,
                },
                new WorkspaceEntry
                {
                    Label = "Claude",
                    Terminal = "it",
                    WtProfile = "Nushell",
                    Command = "claude",
                    IsEnabled = true,
                    Order = 1,
                },
            ],
        };

        var subtitle = ShortcutDisplay.BuildSubtitle(shortcut, _catalog);

        Assert.Contains("2 launches", subtitle, StringComparison.Ordinal);
        Assert.DoesNotContain("npm run dev", subtitle, StringComparison.Ordinal);
        Assert.DoesNotContain("claude", subtitle, StringComparison.Ordinal);
    }
}
