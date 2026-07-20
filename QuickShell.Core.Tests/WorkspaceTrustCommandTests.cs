using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class WorkspaceTrustCommandTests
{
    [Fact]
    public void Grant_then_confirm_with_token_trusts_workspace()
    {
        using var repository = CreateRepository();
        repository.Upsert(CreateWorkspace());
        var workspace = repository.GetByName("Workspace")!;
        Assert.Equal(TrustTransitionStatus.Revoked, repository.RevokeTrust(workspace.Id).Status);

        var services = CreateServices(repository);
        var changed = 0;
        var first = new GrantWorkspaceTrustCommand(workspace.Id, () => changed++, services);
        var firstResult = first.Invoke();

        Assert.Equal(CommandResultKind.Confirm, firstResult.Kind);
        Assert.NotNull(firstResult.Args);
        var confirm = Assert.IsType<ConfirmationArgs>(firstResult.Args);
        var confirmCommand = Assert.IsType<GrantWorkspaceTrustCommand>(confirm.PrimaryCommand);

        var secondResult = confirmCommand.Invoke();

        Assert.Equal(CommandResultKind.KeepOpen, secondResult.Kind);
        Assert.Equal(1, changed);
        Assert.True(repository.GetStoredWorkspace(workspace.Id)!.Security.IsTrusted);
    }

    [Fact]
    public void Grant_with_stale_token_stays_open_without_trusting()
    {
        using var repository = CreateRepository();
        repository.Upsert(CreateWorkspace());
        var workspace = repository.GetByName("Workspace")!;
        Assert.Equal(TrustTransitionStatus.Revoked, repository.RevokeTrust(workspace.Id).Status);

        var review = repository.BeginTrustReview(workspace.Id);
        Assert.NotNull(review.Token);

        var edited = repository.GetById(workspace.Id)!;
        edited.Command = "echo changed";
        edited.Launches[0].Command = "echo changed";
        repository.Upsert(edited, "Workspace");

        var services = CreateServices(repository);
        var changed = 0;
        var command = new GrantWorkspaceTrustCommand(workspace.Id, () => changed++, services, review.Token);
        var result = command.Invoke();

        Assert.Equal(CommandResultKind.KeepOpen, result.Kind);
        Assert.Equal(0, changed);
        Assert.False(repository.GetStoredWorkspace(workspace.Id)!.Security.IsTrusted);
    }

    [Fact]
    public void Revoke_clears_trust()
    {
        using var repository = CreateRepository();
        repository.Upsert(CreateWorkspace());
        var workspace = repository.GetByName("Workspace")!;
        Assert.True(repository.GetStoredWorkspace(workspace.Id)!.Security.IsTrusted);

        var services = CreateServices(repository);
        var changed = 0;
        var result = new RevokeWorkspaceTrustCommand(workspace.Id, () => changed++, services).Invoke();

        Assert.Equal(CommandResultKind.KeepOpen, result.Kind);
        Assert.Equal(1, changed);
        Assert.False(repository.GetStoredWorkspace(workspace.Id)!.Security.IsTrusted);
    }

    private static QuickShellServices CreateServices(IShortcutRepository repository)
    {
        var lifetime = new QuickShellLifetime();
        var drafts = new ShortcutDraftStore(repository);
        return TestQuickShellServicesFactory.Create(
            repository,
            drafts,
            new QuickShellSettingsManager(),
            new FakeProjectAnalysisService(),
            lifetime);
    }

    private static ShortcutRepository CreateRepository()
    {
        var directory = Path.Join(Path.GetTempPath(), "QuickShellTrustCommandTests", Guid.NewGuid().ToString("N"));
        return new ShortcutRepository(directory);
    }

    private static TerminalShortcut CreateWorkspace() =>
        new()
        {
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
