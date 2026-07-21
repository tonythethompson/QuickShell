using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Abstractions;
using QuickShell.Commands;
using QuickShell.Models;
using QuickShell.Pages;
using QuickShell.Services;
using QuickShell.Services.WorkspaceEditor;
using Xunit.Sdk;

namespace QuickShell.Core.Tests.Flows;

public sealed class FormEditFlowTests : IDisposable
{
    private readonly QuickShellLifetime _lifetime = new();
    private readonly TempDataDirectory _temp = new();
    private readonly FakeShortcutRepository _repository;
    private readonly ShortcutDraftStore _drafts;
    private readonly QuickShellServices _services;

    public FormEditFlowTests()
    {
        _repository = new FakeShortcutRepository([], _temp.Path);
        _drafts = new ShortcutDraftStore(_repository);
        _services = TestQuickShellServicesFactory.Create(
            _repository,
            _drafts,
            new QuickShellSettingsManager(),
            new FakeProjectAnalysisService(),
            _lifetime);
    }

    public void Dispose()
    {
        _lifetime.Dispose();
        _drafts.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public void Create_edit_save_persists_workspace()
    {
        var editor = _services.WorkspaceEditors.Create();
        editor.ResetForOpen(null, new TerminalShortcut { Directory = _temp.Path });

        var payload = "{" +
            "\"Name\": \"FlowProject\"," +
            "\"Directory\": \"" + JsonEscaped(_temp.Path) + "\"," +
            "\"LaunchCommand_0\": \"npm start\"" +
            "}";
        Assert.True(editor.TryApplyInputs(payload));

        var result = editor.Save();

        Assert.Equal(WorkspaceEditResultKind.Saved, result.Kind);
        var saved = _repository.GetByName("FlowProject");
        Assert.NotNull(saved);
        Assert.Equal(_temp.Path, saved!.Directory);
    }

    [Fact]
    public void Form_local_undo_restores_original_count_after_adding_companion_row()
    {
        var editor = _services.WorkspaceEditors.Create();
        editor.ResetForOpen(null, new TerminalShortcut { Directory = _temp.Path, Name = "UndoFlow" });
        var before = editor.GetState().Companions.Count;

        editor.AddCompanionRow();
        Assert.Equal(before + 1, editor.GetState().Companions.Count);
        Assert.True(editor.CanUndo);

        Assert.True(editor.TryUndo());
        Assert.Equal(before, editor.GetState().Companions.Count);
    }

    [Fact]
    public void Cancel_with_unsaved_changes_prompts_discard()
    {
        var editor = _services.WorkspaceEditors.Create();
        editor.ResetForOpen(null, new TerminalShortcut { Directory = _temp.Path, Name = "Original" });
        Assert.True(editor.TryApplyInputs("""{"Name":"Changed"}"""));

        var result = editor.Cancel();

        Assert.Equal(WorkspaceEditResultKind.PromptDiscard, result.Kind);
        Assert.True(editor.HasUnsavedChanges);
    }

    private static string JsonEscaped(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private sealed class TempDataDirectory : IDisposable
    {
        public TempDataDirectory()
        {
            Path = System.IO.Path.Join(System.IO.Path.GetTempPath(), "quickshell-form-flow-tests", Guid.NewGuid().ToString("N"));
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
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

[Collection("ShortcutRepositoryMutex")]
public sealed class TrustLaunchFlowTests
{
    [Fact]
    public void Untrusted_grant_launch_revoke_blocks_again()
    {
        using var repository = CreateRepository();
        var folder = Path.Join(Path.GetTempPath(), "QuickShellTrustFlowDir", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            repository.Upsert(CreateWorkspace(folder));
            var workspace = repository.GetByName("TrustFlow")!;
            Assert.Equal(TrustTransitionStatus.Revoked, repository.RevokeTrust(workspace.Id).Status);

            var executor = new CapturingLaunchExecutor();
            var service = new WorkspaceLaunchService(repository, executor, new NoopCompanionLauncher());

            var blocked = service.Launch(workspace.Id, "system", string.Empty);
            Assert.False(blocked.Dismiss);
            Assert.Null(executor.LaunchedShortcut);

            var review = repository.BeginTrustReview(workspace.Id);
            Assert.NotNull(review.Token);
            Assert.Equal(TrustTransitionStatus.Granted, repository.GrantTrust(workspace.Id, review.Token!).Status);

            var allowed = service.Launch(workspace.Id, "system", string.Empty);
            Assert.True(allowed.Dismiss);
            Assert.NotNull(executor.LaunchedShortcut);

            Assert.Equal(TrustTransitionStatus.Revoked, repository.RevokeTrust(workspace.Id).Status);
            executor.Reset();
            var blockedAgain = service.Launch(workspace.Id, "system", string.Empty);
            Assert.False(blockedAgain.Dismiss);
            Assert.Null(executor.LaunchedShortcut);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public void Grant_command_two_step_then_revoke_command()
    {
        using var repository = CreateRepository();
        var folder = Path.Combine(Path.GetTempPath(), "QuickShellTrustFlowDir", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            repository.Upsert(CreateWorkspace(folder));
            var workspace = repository.GetByName("TrustFlow")!;
            Assert.Equal(TrustTransitionStatus.Revoked, repository.RevokeTrust(workspace.Id).Status);

            using var lifetime = new QuickShellLifetime();
            using var drafts = new ShortcutDraftStore(repository);
            var services = TestQuickShellServicesFactory.Create(
                repository,
                drafts,
                new QuickShellSettingsManager(),
                new FakeProjectAnalysisService(),
                lifetime);

            var grant = new GrantWorkspaceTrustCommand(workspace.Id, () => { }, services);
            var confirmResult = grant.Invoke();
            Assert.Equal(CommandResultKind.Confirm, confirmResult.Kind);
            var args = Assert.IsType<ConfirmationArgs>(confirmResult.Args);
            Assert.IsType<GrantWorkspaceTrustCommand>(args.PrimaryCommand).Invoke();
            Assert.True(repository.GetStoredWorkspace(workspace.Id)!.Security.IsTrusted);

            new RevokeWorkspaceTrustCommand(workspace.Id, () => { }, services).Invoke();
            Assert.False(repository.GetStoredWorkspace(workspace.Id)!.Security.IsTrusted);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"Failed to delete temp test directory '{folder}' due to IO error: {ex}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine($"Failed to delete temp test directory '{folder}' due to access error: {ex}");
            }
        }
    }

    private static ShortcutRepository CreateRepository()
    {
        var directory = Path.Join(Path.GetTempPath(), "QuickShellTrustFlowTests", Guid.NewGuid().ToString("N"));
        return new ShortcutRepository(directory);
    }

    private static TerminalShortcut CreateWorkspace(string directory) =>
        new()
        {
            Name = "TrustFlow",
            Directory = directory,
            Command = "echo one",
            Launches =
            [
                new WorkspaceEntry
                {
                    Id = "launch-1",
                    Label = "Launch",
                    Terminal = "default",
                    Command = "echo one",
                    IsEnabled = true,
                },
            ],
        };

    private sealed class CapturingLaunchExecutor : IShortcutLaunchExecutor
    {
        public TerminalShortcut? LaunchedShortcut { get; private set; }

        public void Reset() => LaunchedShortcut = null;

        public ShortcutLaunchResult Launch(
            TerminalShortcut shortcut,
            string terminalApplicationId,
            string defaultProfileId,
            ShortcutLaunchOptions? options = null)
        {
            LaunchedShortcut = shortcut;
            return ShortcutLaunchResult.Dismissed();
        }

        public ShortcutLaunchResult LaunchEntry(
            TerminalShortcut shortcut,
            WorkspaceEntry launch,
            string terminalApplicationId,
            string defaultProfileId,
            ShortcutLaunchOptions? options = null)
        {
            LaunchedShortcut = shortcut;
            return ShortcutLaunchResult.Dismissed();
        }
    }

    private sealed class NoopCompanionLauncher : ICompanionAppLauncher
    {
        public bool IsConfigured(TerminalShortcut shortcut) => false;

        public bool ShouldLaunchOnWorkspaceOpen(TerminalShortcut shortcut) => false;

        public CompanionLaunchResult Launch(TerminalShortcut shortcut, bool onDemand) =>
            new(true, [], null);

        public bool TryLaunch(TerminalShortcut shortcut, bool onDemand, out string? error)
        {
            error = null;
            return true;
        }

        public string BuildDisplaySummary(TerminalShortcut shortcut) => string.Empty;
    }
}

[Collection("ImportConflictState")]
public sealed class ImportConflictFlowTests : IDisposable
{
    private readonly TempDataDirectory _temp = new();
    private readonly ShortcutRepository _repository;
    private readonly QuickShellLifetime _lifetime = new();
    private readonly ShortcutDraftStore _drafts;
    private readonly QuickShellServices _services;

    public ImportConflictFlowTests()
    {
        ImportConflictState.Clear();
        _repository = new ShortcutRepository(_temp.Path);
        _drafts = new ShortcutDraftStore(_repository);
        _services = TestQuickShellServicesFactory.Create(
            _repository,
            _drafts,
            new QuickShellSettingsManager(),
            new FakeProjectAnalysisService(),
            _lifetime);
    }

    public void Dispose()
    {
        ImportConflictState.Clear();
        _lifetime.Dispose();
        _drafts.Dispose();
        _repository.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public void Merge_resolves_name_conflicts()
    {
        var folder = Path.Combine(_temp.Path, "Existing");
        Directory.CreateDirectory(folder);
        _repository.Upsert(CreateShortcut("Alpha", folder));
        Assert.NotNull(_repository.GetByName("Alpha"));

        var importPath = Path.Join(_temp.Path, "incoming.json");
        File.WriteAllText(importPath, """
            [
              {
                "Name": "Alpha",
                "Directory": "C:\\Other"
              },
              {
                "Name": "Beta",
                "Directory": "C:\\Other2"
              }
            ]
            """);

        var direct = _repository.ImportMerge(importPath);
        Assert.True(direct.Success, direct.Message);
        Assert.Contains(_repository.GetShortcuts(), s => s.Name.Equals("Alpha Copy", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(_repository.GetShortcuts(), s => s.Name.Equals("Beta", StringComparison.OrdinalIgnoreCase));

        // Second import with conflicts goes through the conflict form merge action.
        var secondImportPath = Path.Join(_temp.Path, "incoming2.json");
        File.WriteAllText(secondImportPath, """
            [
              {
                "Name": "Alpha",
                "Directory": "C:\\Other3"
              }
            ]
            """);

        var reloaded = 0;
        ImportConflictState.Set(ImportTransferKind.Projects, secondImportPath, conflictCount: 1, importCount: 1, onReload: () => { });
        var form = new ImportConflictForm(_services, () => reloaded++);
        var result = form.SubmitForm("""{"action":"merge"}""", """{"action":"merge"}""");

        Assert.Equal(CommandResultKind.KeepOpen, result.Kind);
        Assert.True(reloaded >= 1, "Expected import conflict form merge to reload the workspace list.");
        Assert.False(ImportConflictState.HasPending);
    }

    [Fact]
    public void Cancel_clears_pending_without_importing()
    {
        var importPath = Path.Join(_temp.Path, "incoming.json");
        File.WriteAllText(importPath, """[{ "Name": "Only", "Directory": "C:\\Only" }]""");
        ImportConflictState.Set(ImportTransferKind.Projects, importPath, 1, 1, () => { });

        var form = new ImportConflictForm(_services, () => { });
        var result = form.SubmitForm("""{"action":"cancel"}""", """{"action":"cancel"}""");

        Assert.Equal(CommandResultKind.KeepOpen, result.Kind);
        Assert.False(ImportConflictState.HasPending);
        Assert.Empty(_repository.GetShortcuts());
    }

    [Fact]
    public void Replace_without_pending_stays_open()
    {
        ImportConflictState.Clear();
        var form = new ImportConflictForm(_services, () => { });
        var result = form.SubmitForm("""{"action":"replace"}""", """{"action":"replace"}""");

        Assert.Equal(CommandResultKind.KeepOpen, result.Kind);
    }

    private static TerminalShortcut CreateShortcut(string name, string directory) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Directory = directory,
        };

    private sealed class TempDataDirectory : IDisposable
    {
        public TempDataDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "quickshell-import-flow-tests", Guid.NewGuid().ToString("N"));
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
            catch (IOException ex)
            {
                throw new XunitException($"Failed to delete temporary test directory '{Path}'.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new XunitException($"Insufficient permissions to delete temporary test directory '{Path}'.", ex);
            }
        }
    }
}

/// <summary>Serializes tests that mutate the process-wide <see cref="ImportConflictState"/>.</summary>
[CollectionDefinition("ImportConflictState", DisableParallelization = true)]
#pragma warning disable CA1711 // Collection fixture name ends with Collection by xUnit convention.
public sealed class ImportConflictStateCollection : ICollectionFixture<object>
#pragma warning restore CA1711
{
}
