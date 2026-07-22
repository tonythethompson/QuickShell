using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Classification;
using QuickShell.Commands;
using QuickShell.Composition;
using QuickShell.Models;
using QuickShell.Pages;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

[Collection("ShortcutRepositoryMutex")]
public sealed class ShortcutListItemsTests
{
    [Fact]
    public void CreateOpen_MissingDirectory_UsesStructuralRepairStateForAllContextMenus()
    {
        var configDirectory = Path.Join(
            Path.GetTempPath(),
            "qs-list-item-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(configDirectory);

        try
        {
            var services = new ServiceCollection();
            services.AddQuickShellCore(configDirectory);
            using var provider = services.BuildServiceProvider();
            var repository = (ShortcutRepository)provider.GetRequiredService<IShortcutRepository>();
            var drafts = (ShortcutDraftStore)provider.GetRequiredService<IDraftStore>();
            var analysis = provider.GetRequiredService<IProjectAnalysisService>();
            var settings = new QuickShellSettingsManager();
            var lifetime = provider.GetRequiredService<IQuickShellLifetime>();
            var quickShellServices = TestQuickShellServicesFactory.CreateFromProvider(
                provider,
                repository,
                drafts,
                settings,
                analysis,
                lifetime);
            var context = new QuickShellPageContext(
                new QuickShellHostServices(quickShellServices),
                new CreateShortcutCommand(() => { }, quickShellServices),
                () => { });
            var shortcut = new TerminalShortcut
            {
                Id = "missing",
                Name = "Missing",
                Directory = Path.Join(configDirectory, "does-not-exist"),
            };

            var item = ShortcutListItems.CreateOpen(
                context,
                shortcut,
                onChanged: () => { },
                includeEdit: false);

            Assert.NotNull(item.MoreCommands);
            Assert.Contains(
                item.MoreCommands.OfType<CommandContextItem>(),
                command => command.Title == Strings.Menu_CreateWorkspace);
        }
        finally
        {
            Directory.Delete(configDirectory, recursive: true);
        }
    }

    [Fact]
    public void CreateOpen_TrustDisabled_SkipsGetStoredWorkspaceAndOmitsUntrustedPrefix()
    {
        using var trustScope = WorkspaceTrustFeatures.DisableForTests();
        var (context, repository, shortcut) = CreateListContextWithUntrustedWorkspace();

        var item = ShortcutListItems.CreateOpen(context, shortcut);

        Assert.Equal(0, repository.GetStoredWorkspaceCallCount);
        Assert.DoesNotContain("Untrusted · ", item.Subtitle, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateOpen_TrustEnabled_Untrusted_PrefixesSubtitleAndLooksUpStoredWorkspace()
    {
        using var trustScope = WorkspaceTrustFeatures.EnableForTests();
        var (context, repository, shortcut) = CreateListContextWithUntrustedWorkspace();

        var item = ShortcutListItems.CreateOpen(context, shortcut);

        Assert.True(repository.GetStoredWorkspaceCallCount >= 1);
        Assert.StartsWith("Untrusted · ", item.Subtitle, StringComparison.Ordinal);
    }

    private static (QuickShellPageContext Context, FakeShortcutRepository Repository, TerminalShortcut Shortcut)
        CreateListContextWithUntrustedWorkspace()
    {
        var root = Path.Join(Path.GetTempPath(), "qs-list-trust-" + Guid.NewGuid().ToString("N"));
        var shortcut = new TerminalShortcut
        {
            Id = "ws-untrusted",
            Name = "Untrusted",
            Directory = root,
            Command = "echo hi",
        };
        var repository = new FakeShortcutRepository([shortcut], root);
        repository.SetSecurity(
            shortcut.Id,
            new WorkspaceSecurityMetadata { IsTrusted = false, Revision = 1 });

        var services = TestQuickShellServicesFactory.Create(
            repository,
            new ShortcutDraftStore(repository),
            new QuickShellSettingsManager(),
            new FakeProjectAnalysisService(),
            new QuickShellLifetime());
        var context = new QuickShellPageContext(
            new QuickShellHostServices(services),
            new CreateShortcutCommand(() => { }, services),
            () => { });
        return (context, repository, shortcut);
    }
}
