using QuickShell.Pages;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class ShortcutFormLaunchSectionTests
{
    [Fact]
    public void ToLaunchInputs_TrimsTrailingPlaceholderBlankRowWithNoTaskType()
    {
        var rows = new List<LaunchRowDraft>
        {
            new() { Command = "npm start" },
            new() { Command = string.Empty, TaskType = TaskTypeCatalog.None, IsEditorPlaceholder = true },
        };

        var inputs = ShortcutFormLaunchSection.ToLaunchInputs(rows, "App", "default", runAsAdmin: false);

        Assert.Single(inputs);
        Assert.Equal("npm start", inputs[0].Command);
    }

    [Fact]
    public void ToLaunchInputs_TrimsMiddlePlaceholderBlankRowWithNoTaskType()
    {
        var rows = new List<LaunchRowDraft>
        {
            new() { Command = "npm start" },
            new() { Command = string.Empty, TaskType = TaskTypeCatalog.None, IsEditorPlaceholder = true },
            new() { Command = "npm test" },
        };

        var inputs = ShortcutFormLaunchSection.ToLaunchInputs(rows, "App", "default", runAsAdmin: false);

        Assert.Equal(2, inputs.Count);
        Assert.Equal("npm start", inputs[0].Command);
        Assert.Equal("npm test", inputs[1].Command);
    }

    [Fact]
    public void ToLaunchInputs_PreservesIntentionalBlankRowWithNoTaskType()
    {
        var rows = new List<LaunchRowDraft>
        {
            new() { Command = "npm start" },
            new() { Command = string.Empty, TaskType = TaskTypeCatalog.None, LaunchTarget = "wt:pwsh" },
        };

        var inputs = ShortcutFormLaunchSection.ToLaunchInputs(rows, "App", "default", runAsAdmin: false);

        Assert.Equal(2, inputs.Count);
        Assert.Equal(string.Empty, inputs[1].Command);
        Assert.Equal("wt:pwsh", inputs[1].LaunchTarget);
    }

    [Fact]
    public void EnsureMinimumRows_PadsToThree()
    {
        var rows = new List<LaunchRowDraft> { new() { Command = "npm start" } };

        LaunchRowListEditor.EnsureMinimumRowsForEditor(rows, "default");

        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void ToLaunchInputs_RetainsTrailingBlankRowWithTypedTaskType()
    {
        var rows = new List<LaunchRowDraft>
        {
            new() { Command = "npm start" },
            new() { Command = string.Empty, TaskType = TaskTypeCatalog.Services },
        };

        var inputs = ShortcutFormLaunchSection.ToLaunchInputs(rows, "App", "default", runAsAdmin: false);

        Assert.Equal(2, inputs.Count);
        Assert.Equal(string.Empty, inputs[1].Command);
        Assert.Equal(TaskTypeCatalog.Services, inputs[1].TaskType);
    }

    [Fact]
    public void TryCreateCommandFromTaskType_PrefillsSuggestedCommand()
    {
        var directory = Path.Combine(Path.GetTempPath(), "quickshell-task-fill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "docker-compose.yml"), "services: {}");

        try
        {
            var created = ShortcutFormLaunchSection.TryCreateCommandFromTaskType(directory, TaskTypeCatalog.Logs);

            Assert.NotNull(created);
            Assert.Equal("docker compose logs -f", created!.Command);
            Assert.Equal(TaskTypeCatalog.Logs, created.TaskType);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void TryCreateCommandFromTaskType_None_ReturnsNull()
    {
        Assert.Null(ShortcutFormLaunchSection.TryCreateCommandFromTaskType(@"C:\temp", TaskTypeCatalog.None));
    }
}
