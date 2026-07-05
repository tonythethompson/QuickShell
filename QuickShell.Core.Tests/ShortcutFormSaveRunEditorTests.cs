using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class ShortcutFormSaveRunEditorTests
{
    [Fact]
    public void TrySaveRunEditor_Create_UsesSingleLaunch()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);
        var folder = Path.Combine(directory.Path, "NewProject");
        Directory.CreateDirectory(folder);

        var result = ShortcutFormSave.TrySaveRunEditor(
            existing: null,
            originalName: null,
            name: "NewProject",
            abbreviation: "np",
            directory: folder,
            command: "npm start",
            launchTarget: "default",
            runAsAdmin: false,
            taskType: TaskTypeCatalog.Frontend,
            repository,
            onSaved: null);

        Assert.True(result.Success);
        var saved = repository.GetByName("NewProject");
        Assert.NotNull(saved);
        Assert.Single(saved!.Launches);
        Assert.Equal("npm start", saved.Launches[0].Command);

        // The Run editor's "create" path delegates to the single-command TrySave
        // overload, which doesn't accept a task type — deliberately out of scope
        // (see WorkspaceSeedFactory/DevServerUrlDetection for smart-setup inference instead).
        Assert.Equal(TaskTypeCatalog.None, saved.Launches[0].TaskType);
    }

    [Fact]
    public void TrySaveRunEditor_Edit_PreservesSecondaryLaunches()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);
        var folder = Path.Combine(directory.Path, "Multi");
        Directory.CreateDirectory(folder);

        var companionPath = Environment.GetEnvironmentVariable("ComSpec")
            ?? @"C:\Windows\System32\cmd.exe";

        var existing = new TerminalShortcut
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Multi",
            Directory = folder,
            Launches =
            [
                new WorkspaceEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Label = "Primary",
                    Command = "dotnet run",
                    Terminal = "default",
                    IsEnabled = true,
                    Order = 0,
                },
                new WorkspaceEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Label = "Agents",
                    Command = "claude",
                    Terminal = "wt",
                    IsEnabled = true,
                    Order = 1,
                },
            ],
            DevServerUrl = "http://localhost:3000",
            OpenDevServerOnLaunch = true,
            OpenCompanionAppOnLaunch = true,
            CompanionAppPath = companionPath,
        };
        ShortcutLaunchNormalization.NormalizeShortcut(existing);
        repository.Upsert(existing);

        var secondaryId = existing.Launches[1].Id;

        var result = ShortcutFormSave.TrySaveRunEditor(
            existing,
            originalName: "Multi",
            name: "Multi",
            abbreviation: string.Empty,
            directory: folder,
            command: "npm run dev",
            launchTarget: "pwsh",
            runAsAdmin: true,
            taskType: TaskTypeCatalog.Api,
            repository,
            onSaved: null);

        Assert.True(result.Success);
        Assert.Contains("preserved", result.Message, StringComparison.OrdinalIgnoreCase);

        var saved = repository.GetByName("Multi");
        Assert.NotNull(saved);
        Assert.Equal(2, saved!.Launches.Count);
        Assert.Equal("npm run dev", saved.Launches[0].Command);
        Assert.True(saved.Launches[0].RunAsAdmin);
        Assert.Equal("pwsh", saved.Launches[0].Terminal);
        Assert.Equal(TaskTypeCatalog.Api, saved.Launches[0].TaskType);
        Assert.Equal("claude", saved.Launches[1].Command);
        Assert.Equal(secondaryId, saved.Launches[1].Id);
        Assert.StartsWith("http://localhost:3000", saved.DevServerUrl);
        Assert.True(saved.OpenDevServerOnLaunch);
        Assert.True(saved.OpenCompanionAppOnLaunch);
        Assert.Equal(companionPath, saved.CompanionAppPath);
    }

    [Fact]
    public void TrySaveRunEditor_Edit_UpdatesPrimaryWhenFirstLaunchDisabled()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);
        var folder = Path.Combine(directory.Path, "DisabledFirst");
        Directory.CreateDirectory(folder);

        var disabledId = Guid.NewGuid().ToString("N");
        var enabledId = Guid.NewGuid().ToString("N");
        var existing = new TerminalShortcut
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "DisabledFirst",
            Directory = folder,
            Launches =
            [
                new WorkspaceEntry
                {
                    Id = disabledId,
                    Label = "Off",
                    Command = "old",
                    Terminal = "cmd",
                    IsEnabled = false,
                    Order = 0,
                },
                new WorkspaceEntry
                {
                    Id = enabledId,
                    Label = "Active",
                    Command = "keep-me",
                    Terminal = "wt",
                    IsEnabled = true,
                    Order = 1,
                },
            ],
        };
        ShortcutLaunchNormalization.NormalizeShortcut(existing);
        repository.Upsert(existing);

        var result = ShortcutFormSave.TrySaveRunEditor(
            existing,
            originalName: "DisabledFirst",
            name: "DisabledFirst",
            abbreviation: string.Empty,
            directory: folder,
            command: "updated",
            launchTarget: "default",
            runAsAdmin: false,
            taskType: TaskTypeCatalog.None,
            repository,
            onSaved: null);

        Assert.True(result.Success);
        var saved = repository.GetByName("DisabledFirst");
        Assert.NotNull(saved);
        Assert.Equal("old", saved!.Launches.First(e => e.Id == disabledId).Command);
        Assert.Equal("updated", saved.Launches.First(e => e.Id == enabledId).Command);
    }

    [Fact]
    public void TrySaveRunEditor_Edit_RepairsInvalidLaunches()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);
        var folder = Path.Combine(directory.Path, "Broken");
        Directory.CreateDirectory(folder);

        var existing = new TerminalShortcut
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Broken",
            Directory = folder,
            DevServerUrl = "http://localhost:5173",
            Launches =
            [
                new WorkspaceEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Label = string.Empty,
                    Command = "old",
                    Terminal = "cmd",
                    IsEnabled = false,
                    Order = 0,
                },
            ],
        };

        var result = ShortcutFormSave.TrySaveRunEditor(
            existing,
            originalName: "Broken",
            name: "Broken",
            abbreviation: string.Empty,
            directory: folder,
            command: "npm run dev",
            launchTarget: "default",
            runAsAdmin: false,
            taskType: TaskTypeCatalog.None,
            repository,
            onSaved: null);

        Assert.True(result.Success);
        var saved = repository.GetByName("Broken");
        Assert.NotNull(saved);
        Assert.Single(saved!.Launches);
        Assert.Equal("Broken", saved.Launches[0].Label);
        Assert.Equal("npm run dev", saved.Launches[0].Command);
        Assert.True(saved.Launches[0].IsEnabled);
        Assert.StartsWith("http://localhost:5173", saved.DevServerUrl);
    }

    [Fact]
    public void TrySaveRunEditor_Edit_RepairsPrimaryWithoutDroppingSecondaryLaunches()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);
        var folder = Path.Combine(directory.Path, "RepairPrimary");
        Directory.CreateDirectory(folder);

        var secondaryId = Guid.NewGuid().ToString("N");
        var existing = new TerminalShortcut
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "RepairPrimary",
            Directory = folder,
            Launches =
            [
                new WorkspaceEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Label = string.Empty,
                    Command = "old",
                    Terminal = "cmd",
                    IsEnabled = false,
                    Order = 0,
                },
                new WorkspaceEntry
                {
                    Id = secondaryId,
                    Label = "Agents",
                    Command = "claude",
                    Terminal = "wt",
                    IsEnabled = true,
                    Order = 1,
                },
            ],
        };

        var result = ShortcutFormSave.TrySaveRunEditor(
            existing,
            originalName: "RepairPrimary",
            name: "RepairPrimary",
            abbreviation: string.Empty,
            directory: folder,
            command: "npm run dev",
            launchTarget: "default",
            runAsAdmin: false,
            taskType: TaskTypeCatalog.None,
            repository,
            onSaved: null);

        Assert.True(result.Success);
        Assert.Contains("Repaired", result.Message, StringComparison.OrdinalIgnoreCase);

        var saved = repository.GetByName("RepairPrimary");
        Assert.NotNull(saved);
        Assert.Equal(2, saved!.Launches.Count);
        Assert.Equal("npm run dev", saved.Launches.First(e => e.Id == secondaryId).Command);
        Assert.Equal("old", saved.Launches.First(e => e.Order == 0).Command);
        Assert.Equal("Disabled", saved.Launches.First(e => e.Order == 0).Label);
    }

    private sealed class TempDataDirectory : IDisposable
    {
        public TempDataDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "quickshell-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
