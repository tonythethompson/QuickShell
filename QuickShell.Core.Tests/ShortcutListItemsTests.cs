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
}
