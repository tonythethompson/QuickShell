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
            repository,
            onSaved: null);

        Assert.True(result.Success);
        var saved = repository.GetByName("NewProject");
        Assert.NotNull(saved);
        Assert.Single(saved!.Launches);
        Assert.Equal("npm start", saved.Launches[0].Command);
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
        Assert.Equal("claude", saved.Launches[1].Command);
        Assert.Equal(secondaryId, saved.Launches[1].Id);
        Assert.StartsWith("http://localhost:3000", saved.DevServerUrl);
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
            repository,
            onSaved: null);

        Assert.True(result.Success);
        var saved = repository.GetByName("DisabledFirst");
        Assert.NotNull(saved);
        Assert.Equal("old", saved!.Launches.First(e => e.Id == disabledId).Command);
        Assert.Equal("updated", saved.Launches.First(e => e.Id == enabledId).Command);
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
            catch
            {
            }
        }
    }
}
