using QuickShell.Pages;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class ShortcutFormLaunchSectionTests
{
    [Fact]
    public void ToLaunchInputs_TrimsTrailingBlankRowWithNoTaskType()
    {
        var rows = new List<ShortcutFormLaunchSection.CommandRowDraft>
        {
            new() { Command = "npm start" },
            new() { Command = string.Empty, TaskType = TaskTypeCatalog.None },
        };

        var inputs = ShortcutFormLaunchSection.ToLaunchInputs(rows, "App", "default", runAsAdmin: false);

        Assert.Single(inputs);
        Assert.Equal("npm start", inputs[0].Command);
    }

    [Fact]
    public void ToLaunchInputs_RetainsTrailingBlankRowWithTypedTaskType()
    {
        var rows = new List<ShortcutFormLaunchSection.CommandRowDraft>
        {
            new() { Command = "npm start" },
            new() { Command = string.Empty, TaskType = TaskTypeCatalog.Database },
        };

        var inputs = ShortcutFormLaunchSection.ToLaunchInputs(rows, "App", "default", runAsAdmin: false);

        Assert.Equal(2, inputs.Count);
        Assert.Equal(string.Empty, inputs[1].Command);
        Assert.Equal(TaskTypeCatalog.Database, inputs[1].TaskType);
    }
}
