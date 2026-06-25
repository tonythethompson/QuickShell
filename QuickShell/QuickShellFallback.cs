using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Models;
using QuickShell.Pages;
using QuickShell.Services;

namespace QuickShell;

internal sealed partial class QuickShellFallback : FallbackCommandItem
{
    private const string CommandId = "com.quickshell.fallback";
    private static readonly NoOpCommand BaseCommand = new() { Id = CommandId };

    private readonly QuickShellFallbackPage _listPage;
    private readonly QuickShellSettingsManager _settings;
    private string _lastQuery = string.Empty;

    public QuickShellFallback(QuickShellFallbackPage listPage, QuickShellSettingsManager settings)
        : base(BaseCommand, "Saved shortcut", CommandId)
    {
        _listPage = listPage;
        _settings = settings;
        Title = string.Empty;
        Subtitle = string.Empty;
        Icon = new IconInfo("\uE756");
    }

    public override void UpdateQuery(string query)
    {
        _lastQuery = query ?? string.Empty;

        if (ShouldSuppress(query))
        {
            ClearResult();
            return;
        }

        var shortcuts = ShortcutStore.Search(_lastQuery).ToArray();
        if (shortcuts.Length == 0)
        {
            ClearResult();
            return;
        }

        _listPage.UpdateSearchText(string.Empty, _lastQuery);

        if (shortcuts.Length == 1)
        {
            ApplyShortcutResult(shortcuts[0]);
            return;
        }

        Title = string.Empty;
        Subtitle = string.Empty;
        Command = _listPage;
    }

    private void ApplyShortcutResult(TerminalShortcut shortcut)
    {
        Title = shortcut.Name;
        Subtitle = ShortcutDisplay.BuildSubtitle(shortcut);
        Icon = new IconInfo("\uE756");
        Command = new OpenTerminalShortcutCommand(shortcut, _settings);

        var moreCommands = new List<CommandContextItem>(
            ShortcutContextCommands.Build(shortcut, ReloadListPage, _settings, includeEdit: false));

        if (!shortcut.RunAsAdmin)
        {
            var adminCommand = new OpenTerminalShortcutCommand(shortcut, _settings, runAsAdmin: true);
            moreCommands.Insert(0, ShortcutContextCommands.CreateOpenAsAdminContextItem(adminCommand));
        }

        MoreCommands = moreCommands.ToArray();
    }

    private void ReloadListPage() => UpdateQuery(_lastQuery);

    private static bool ShouldSuppress(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return query.Contains("quick shell", StringComparison.OrdinalIgnoreCase);
    }

    private void ClearResult()
    {
        Title = string.Empty;
        Subtitle = string.Empty;
        Command = BaseCommand;
        MoreCommands = [];
        _listPage.UpdateSearchText(string.Empty, string.Empty);
    }
}
