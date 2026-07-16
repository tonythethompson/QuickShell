using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Commands;
using QuickShell.Composition;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

/// <summary>
/// Regression: home list items must expose the full context menu (favorite, duplicate,
/// delete, undo/redo, settings, …), not the stripped BuildForHomePin subset that left
/// only elevation / folder / edit.
/// </summary>
[Collection(QuickShellServicesIsolation.Name)]
public sealed class ShortcutContextMenuRegressionTests : IDisposable
{
    private readonly string _configDirectory;
    private readonly ServiceProvider _serviceProvider;
    private readonly ShortcutRepository _repository;
    private readonly QuickShellSettingsManager _settings;
    private readonly CreateShortcutCommand _createCommand;

    public ShortcutContextMenuRegressionTests()
    {
        _configDirectory = Path.Combine(
            Path.GetTempPath(),
            "qs-context-menu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configDirectory);

        var services = new ServiceCollection();
        services.AddQuickShellCore(_configDirectory);
        _serviceProvider = services.BuildServiceProvider();

        _repository = (ShortcutRepository)_serviceProvider.GetRequiredService<IShortcutRepository>();
        var drafts = (ShortcutDraftStore)_serviceProvider.GetRequiredService<IDraftStore>();
        var analysis = _serviceProvider.GetRequiredService<IProjectAnalysisService>();
        _settings = new QuickShellSettingsManager();
        _createCommand = new CreateShortcutCommand(() => { });

        var lifetime = _serviceProvider.GetRequiredService<IQuickShellLifetime>();
        QuickShellServices.Bind(new QuickShellServices(_repository, drafts, _settings, analysis, lifetime));
    }

    public void Dispose()
    {
        QuickShellServices.Unbind();
        _serviceProvider.Dispose();

        try
        {
            if (Directory.Exists(_configDirectory))
            {
                Directory.Delete(_configDirectory, recursive: true);
            }
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }

    [Fact]
    public void Build_IncludesManageAndUtilityCommands()
    {
        var shortcut = CreateHealthyShortcut("Alpha");
        var titles = GetTitles(ShortcutContextCommands.Build(
            shortcut,
            onChanged: () => { },
            _settings,
            _createCommand));

        AssertContainsAll(
            titles,
            Strings.Menu_Edit,
            Strings.Command_Favorite_Name,
            Strings.Command_Duplicate_Name,
            Strings.Command_Delete_Name,
            Strings.Menu_Undo,
            Strings.Menu_Redo,
            Strings.Menu_CreateWorkspace,
            QuickShellBrand.SettingsTitle,
            Strings.Menu_OpenInFileExplorer,
            Strings.Menu_CopyPath,
            Strings.Menu_RunAsAdmin);
    }

    [Fact]
    public void BuildForHomePin_ExcludesPageAndPinnedMoveCommands()
    {
        var shortcut = CreateHealthyShortcut("HomePin");
        var full = GetTitles(ShortcutContextCommands.Build(
            shortcut,
            onChanged: () => { },
            _settings,
            _createCommand));
        var home = GetTitles(ShortcutContextCommands.BuildForHomePin(
            shortcut,
            onChanged: () => { },
            _settings,
            _createCommand));

        Assert.Contains(Strings.Menu_Undo, full);
        Assert.Contains(Strings.Menu_Redo, full);
        Assert.Contains(Strings.Menu_CreateWorkspace, full);
        Assert.DoesNotContain(Strings.Menu_Undo, home);
        Assert.DoesNotContain(Strings.Menu_Redo, home);
        Assert.Contains(Strings.Menu_CreateWorkspace, home);
    }

    [Fact]
    public void CreateOpen_WiresFullMoreCommands_WhenOnChangedProvided()
    {
        var shortcut = CreateHealthyShortcut("ListItem");
        var item = ShortcutListItems.CreateOpen(
            shortcut,
            _settings,
            onChanged: () => { },
            _createCommand);

        Assert.NotNull(item.MoreCommands);
        var titles = GetTitles(item.MoreCommands!);

        AssertContainsAll(
            titles,
            Strings.Menu_Edit,
            Strings.Command_Favorite_Name,
            Strings.Command_Duplicate_Name,
            Strings.Command_Delete_Name,
            Strings.Menu_Undo,
            Strings.Menu_CreateWorkspace,
            QuickShellBrand.SettingsTitle);
    }

    [Fact]
    public void CreateOpen_WithoutOnChanged_LeavesMoreCommandsEmpty()
    {
        var shortcut = CreateHealthyShortcut("NoContext");
        var item = ShortcutListItems.CreateOpen(shortcut, _settings);

        Assert.True(item.MoreCommands is null || item.MoreCommands.Length == 0);
    }

    [Fact]
    public void Build_WithPinnedMoves_IncludesMoveCommandsWhenVisibilityAllows()
    {
        var first = CreateHealthyShortcut("First");
        first.IsPinned = true;
        first.PinOrder = 0;
        var second = CreateHealthyShortcut("Second");
        second.IsPinned = true;
        second.PinOrder = 1;

        var visibility = PinnedMoveVisibility.ForShortcut(second, [first, second]);
        Assert.True(visibility.ShowUp);
        Assert.True(visibility.ShowToTop);

        var titles = GetTitles(ShortcutContextCommands.Build(
            second,
            onChanged: () => { },
            _settings,
            moveVisibility: visibility));

        Assert.Contains("Move up", titles);
        Assert.Contains("Move to top", titles);
    }

    [Fact]
    public void Build_MultiLaunch_AddsPerLaunchOpenCommands()
    {
        var shortcut = CreateHealthyShortcut("Multi");
        shortcut.Launches =
        [
            new WorkspaceEntry
            {
                Id = "1",
                Label = "Frontend",
                Command = "npm run dev",
                IsEnabled = true,
                Order = 0,
            },
            new WorkspaceEntry
            {
                Id = "2",
                Label = "Api",
                Command = "dotnet watch",
                IsEnabled = true,
                Order = 1,
            },
        ];

        var titles = GetTitles(ShortcutContextCommands.Build(
            shortcut,
            onChanged: () => { },
            _settings));

        Assert.Contains("npm run dev", titles);
        Assert.Contains("dotnet watch", titles);
    }

    private TerminalShortcut CreateHealthyShortcut(string name)
    {
        var directory = Path.Combine(_configDirectory, "workspaces", name);
        Directory.CreateDirectory(directory);

        return new TerminalShortcut
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Directory = directory,
            Command = string.Empty,
            IsPinned = false,
        };
    }

    private static List<string> GetTitles(IEnumerable<IContextItem> items) =>
        items
            .OfType<CommandContextItem>()
            .Select(item => item.Title ?? item.Command?.Name ?? string.Empty)
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .ToList();

    private static void AssertContainsAll(IReadOnlyList<string> titles, params string[] expected)
    {
        foreach (var title in expected)
        {
            Assert.True(
                titles.Any(actual => string.Equals(actual, title, StringComparison.Ordinal)),
                $"Expected context menu to include '{title}'. Actual: [{string.Join(", ", titles)}]");
        }
    }
}
