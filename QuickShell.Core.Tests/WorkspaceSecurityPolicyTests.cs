using QuickShell.Abstractions;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class WorkspaceSecurityPolicyTests
{
    [Fact]
    public void Untrusted_workspace_blocks_external_actions_but_allows_copy_path()
    {
        var workspace = new StoredWorkspace(
            CreateWorkspace(),
            new WorkspaceSecurityMetadata { IsTrusted = false, Revision = 4 },
            4);

        var launch = WorkspaceSecurityPolicy.Authorize(workspace, WorkspaceAction.LaunchTerminal);
        var copy = WorkspaceSecurityPolicy.Authorize(workspace, WorkspaceAction.CopyPath);
        var folder = WorkspaceSecurityPolicy.Authorize(workspace, WorkspaceAction.OpenDirectory);

        Assert.False(launch.IsAllowed);
        Assert.Equal(WorkspaceIssueCode.WorkspaceUntrusted, launch.PrimaryIssueCode);
        Assert.True(copy.IsAllowed);
        Assert.False(folder.IsAllowed);
    }

    [Fact]
    public void Terminal_launch_ignores_invalid_optional_effect_configuration()
    {
        var content = CreateWorkspace();
        content.DevServerUrl = "file:///not-http";
        content.RepoUrl = "javascript:alert(1)";
        content.CompanionApps =
        [
            new CompanionAppEntry
            {
                Id = "companion-1",
                Path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.exe"),
                OpenOnLaunch = true,
            },
        ];
        var workspace = new StoredWorkspace(content, new WorkspaceSecurityMetadata(), 1);

        var terminal = WorkspaceSecurityPolicy.Authorize(workspace, WorkspaceAction.LaunchTerminal);
        var companion = WorkspaceSecurityPolicy.Authorize(workspace, WorkspaceAction.StartCompanion);
        var devServer = WorkspaceSecurityPolicy.AuthorizeUrl(
            workspace,
            content.DevServerUrl,
            WorkspaceAction.OpenDevServer);
        var repository = WorkspaceSecurityPolicy.AuthorizeUrl(workspace, content.RepoUrl);
        var trustReview = WorkspaceSecurityPolicy.Authorize(workspace, WorkspaceAction.GrantTrust);

        Assert.True(terminal.IsAllowed);
        Assert.False(companion.IsAllowed);
        Assert.Equal(WorkspaceIssueCode.CompanionExecutableUnavailable, companion.PrimaryIssueCode);
        Assert.False(devServer.IsAllowed);
        Assert.Equal(WorkspaceIssueCode.InvalidUrl, devServer.PrimaryIssueCode);
        Assert.False(repository.IsAllowed);
        Assert.Equal(WorkspaceIssueCode.InvalidUrl, repository.PrimaryIssueCode);
        Assert.False(trustReview.IsAllowed);
    }

    [Fact]
    public void Launch_service_suppresses_only_denied_optional_effects()
    {
        var content = CreateWorkspace();
        content.DevServerUrl = "file:///not-http";
        content.OpenDevServerOnLaunch = true;
        content.CompanionApps =
        [
            new CompanionAppEntry
            {
                Id = "companion-1",
                Path = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.exe"),
                OpenOnLaunch = true,
            },
        ];
        var executor = new CapturingLaunchExecutor();
        var service = new WorkspaceLaunchService(
            new FakeShortcutRepository([content]),
            executor,
            new ConfiguredCompanionLauncher());

        var result = service.Launch(
            content.Id,
            "system",
            string.Empty,
            new ShortcutLaunchOptions(IncludeCompanionApp: true, IncludeDevServerLink: true));

        Assert.True(result.Dismiss);
        Assert.NotNull(executor.LaunchedShortcut);
        Assert.False(executor.Options.IncludeCompanionApp);
        Assert.False(executor.Options.IncludeDevServerLink);
    }

    [Fact]
    public void Launch_service_applies_effective_values_from_companion_authorization()
    {
        var content = CreateWorkspace();
        content.CompanionApps =
        [
            new CompanionAppEntry
            {
                Id = "companion-1",
                Path = Environment.ProcessPath,
                Arguments = "{folder}",
                OpenOnLaunch = true,
            },
        ];
        var executor = new CapturingLaunchExecutor();
        var service = new WorkspaceLaunchService(
            new FakeShortcutRepository([content]),
            executor,
            new ConfiguredCompanionLauncher());

        var result = service.Launch(
            content.Id,
            "system",
            string.Empty,
            new ShortcutLaunchOptions(IncludeCompanionApp: true));

        Assert.True(result.Dismiss);
        var launchedCompanion = Assert.Single(executor.LaunchedShortcut!.CompanionApps);
        Assert.Equal(Path.GetFullPath(Environment.ProcessPath!), launchedCompanion.Path);
        Assert.Contains(Path.GetFullPath(content.Directory), launchedCompanion.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.True(executor.Options.IncludeCompanionApp);
    }

    [Fact]
    public void Launch_service_retains_and_canonicalizes_valid_companion_sibling()
    {
        var content = CreateWorkspace();
        content.CompanionApps =
        [
            new CompanionAppEntry
            {
                Id = "invalid-companion",
                Path = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.exe"),
                OpenOnLaunch = true,
                Order = 0,
            },
            new CompanionAppEntry
            {
                Id = "valid-companion",
                Path = Environment.ProcessPath,
                Arguments = "{folder}",
                OpenOnLaunch = true,
                Order = 1,
            },
        ];
        var workspace = new StoredWorkspace(content, new WorkspaceSecurityMetadata(), 1);
        var executor = new CapturingLaunchExecutor();
        var service = new WorkspaceLaunchService(
            new FakeShortcutRepository([content]),
            executor,
            new ConfiguredCompanionLauncher());

        var aggregateAuthorization = WorkspaceSecurityPolicy.Authorize(
            workspace,
            WorkspaceAction.StartCompanion);
        var result = service.Launch(
            content.Id,
            "system",
            string.Empty,
            new ShortcutLaunchOptions(IncludeCompanionApp: true));

        Assert.False(aggregateAuthorization.IsAllowed);
        Assert.True(result.Dismiss);
        Assert.True(executor.Options.IncludeCompanionApp);
        var launchedCompanion = Assert.Single(executor.LaunchedShortcut!.CompanionApps);
        Assert.Equal("valid-companion", launchedCompanion.Id);
        Assert.Equal(Path.GetFullPath(Environment.ProcessPath!), launchedCompanion.Path);
        Assert.Contains(Path.GetFullPath(content.Directory), launchedCompanion.Arguments, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Launch_service_does_not_retain_denied_companion_with_duplicate_id()
    {
        var duplicateId = "duplicate-companion";
        var missingPath = Path.Join(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"),
            "missing.exe");
        var content = CreateWorkspace();
        content.CompanionApps =
        [
            new CompanionAppEntry
            {
                Id = duplicateId,
                Path = Environment.ProcessPath,
                Arguments = "{folder}",
                OpenOnLaunch = true,
                Order = 0,
            },
            new CompanionAppEntry
            {
                Id = duplicateId,
                Path = missingPath,
                Arguments = "denied",
                OpenOnLaunch = true,
                Order = 1,
            },
        ];
        var executor = new CapturingLaunchExecutor();
        var service = new WorkspaceLaunchService(
            new FakeShortcutRepository([content]),
            executor,
            new ConfiguredCompanionLauncher());

        var result = service.Launch(
            content.Id,
            "system",
            string.Empty,
            new ShortcutLaunchOptions(IncludeCompanionApp: true));

        Assert.True(result.Dismiss);
        Assert.True(executor.Options.IncludeCompanionApp);
        var launchedCompanion = Assert.Single(executor.LaunchedShortcut!.CompanionApps);
        Assert.Equal(duplicateId, launchedCompanion.Id);
        Assert.Equal(Path.GetFullPath(Environment.ProcessPath!), launchedCompanion.Path);
        Assert.DoesNotContain("denied", launchedCompanion.Arguments, StringComparison.Ordinal);
        Assert.NotEqual(missingPath, launchedCompanion.Path);
    }

    [Fact]
    public void Selected_entry_launch_ignores_invalid_sibling_entry()
    {
        var content = CreateWorkspace();
        content.Launches.Add(new WorkspaceEntry
        {
            Id = "invalid-sibling",
            Label = "Invalid",
            Command = "echo invalid\ncommand",
            IsEnabled = true,
        });
        var executor = new CapturingLaunchExecutor();
        var service = new WorkspaceLaunchService(
            new FakeShortcutRepository([content]),
            executor,
            new ConfiguredCompanionLauncher());

        var result = service.LaunchEntry(
            content.Id,
            "launch-1",
            "system",
            string.Empty);

        Assert.True(result.Dismiss);
        Assert.Equal("launch-1", executor.LaunchedEntry?.Id);
    }

    [Theory]
    [InlineData("\\\\server\\share")]
    [InlineData("\\\\.\\pipe\\quickshell")]
    [InlineData("%TEMP%\\workspace")]
    public void Open_directory_rejects_non_local_path_namespaces(string directory)
    {
        var content = CreateWorkspace();
        content.Directory = directory;
        var workspace = new StoredWorkspace(content, new WorkspaceSecurityMetadata(), 1);

        var result = WorkspaceSecurityPolicy.Authorize(workspace, WorkspaceAction.OpenDirectory);

        Assert.False(result.IsAllowed);
        Assert.Equal(WorkspaceIssueCode.DirectoryOpenNotAllowed, result.PrimaryIssueCode);
    }

    [Fact]
    public void Trust_review_token_is_invalidated_by_revision_change()
    {
        using var repository = CreateRepository();
        repository.Upsert(CreateWorkspace());
        var stored = repository.GetStoredWorkspace(repository.GetByName("Workspace")!.Id)!;
        repository.RevokeTrust(stored.Content.Id);

        var review = repository.BeginTrustReview(stored.Content.Id);
        Assert.NotNull(review.Token);

        var edited = repository.GetById(stored.Content.Id)!;
        edited.Command = "echo changed";
        edited.Launches[0].Command = "echo changed";
        repository.Upsert(edited, "Workspace");

        var transition = repository.GrantTrust(stored.Content.Id, review.Token!);
        Assert.Equal(TrustTransitionStatus.WorkspaceChangedSinceReview, transition.Status);
    }

    [Fact]
    public void Stale_editor_save_cannot_restore_revoked_trust()
    {
        using var repository = CreateRepository();
        repository.Upsert(CreateWorkspace());
        var workspaceId = repository.GetByName("Workspace")!.Id;
        var staleEditor = repository.GetById(workspaceId)!;

        Assert.Equal(TrustTransitionStatus.Revoked, repository.RevokeTrust(workspaceId).Status);
        staleEditor.Command = "echo from stale editor";
        staleEditor.Launches[0].Command = "echo from stale editor";
        repository.Upsert(staleEditor, "Workspace");

        Assert.False(repository.GetStoredWorkspace(workspaceId)!.Security.IsTrusted);
    }

    [Fact]
    public void Imported_workspace_is_untrusted_even_when_local_store_is_trusted()
    {
        using var repository = CreateRepository();
        repository.Upsert(CreateWorkspace());
        var importPath = Path.Join(repository.ConfigDirectory, "import.json");
        var imported = CreateWorkspace();
        imported.Id = "imported-workspace";
        imported.Name = "Imported";
        File.WriteAllText(importPath, System.Text.Encoding.UTF8.GetString(ShortcutLayoutJson.Serialize(
            [ShortcutLayoutEntry.FromShortcut(imported)])));

        var result = repository.ImportMerge(importPath);

        Assert.True(result.Success);
        var importedStored = repository.GetByName("Imported")!;
        Assert.False(repository.GetStoredWorkspace(importedStored.Id)!.Security.IsTrusted);
    }

    [Fact]
    public void Duplicate_preserves_source_trust_state()
    {
        using var repository = CreateRepository();
        repository.Upsert(CreateWorkspace());
        var source = repository.GetByName("Workspace")!;
        Assert.Equal(TrustTransitionStatus.Revoked, repository.RevokeTrust(source.Id).Status);

        var duplicate = repository.BuildDuplicateFrom(source);
        repository.Upsert(duplicate);

        var storedDuplicate = repository.GetStoredWorkspace(duplicate.Id);
        Assert.NotNull(storedDuplicate);
        Assert.False(storedDuplicate.Security.IsTrusted);
    }

    private static ShortcutRepository CreateRepository()
    {
        var directory = Path.Join(Path.GetTempPath(), "QuickShellSecurityTests", Guid.NewGuid().ToString("N"));
        return new ShortcutRepository(directory);
    }

    private static TerminalShortcut CreateWorkspace() =>
        new()
        {
            Id = "workspace-1",
            Name = "Workspace",
            Directory = Path.GetTempPath(),
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

        public WorkspaceEntry? LaunchedEntry { get; private set; }

        public ShortcutLaunchOptions Options { get; private set; }

        public ShortcutLaunchResult Launch(
            TerminalShortcut shortcut,
            string terminalApplicationId,
            string defaultProfileId,
            ShortcutLaunchOptions? options = null)
        {
            LaunchedShortcut = shortcut;
            Options = options ?? default;
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
            LaunchedEntry = launch;
            Options = options ?? default;
            return ShortcutLaunchResult.Dismissed();
        }
    }

    private sealed class ConfiguredCompanionLauncher : ICompanionAppLauncher
    {
        public bool IsConfigured(TerminalShortcut shortcut) => true;

        public bool ShouldLaunchOnWorkspaceOpen(TerminalShortcut shortcut) => true;

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
