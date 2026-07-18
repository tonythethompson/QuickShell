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
        var importPath = Path.Combine(repository.ConfigDirectory, "import.json");
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
        var directory = Path.Combine(Path.GetTempPath(), "QuickShellSecurityTests", Guid.NewGuid().ToString("N"));
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
}
