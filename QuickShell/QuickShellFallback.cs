using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;
using QuickShell.Pages;
using QuickShell.Services;

namespace QuickShell;

internal sealed partial class QuickShellFallback : FallbackCommandItem
{
    private const string CommandId = "com.quickshell.fallback";
    private static readonly NoOpCommand BaseCommand = new() { Id = CommandId };

    private readonly QuickShellFallbackPage _listPage;
    private string _lastQuery = string.Empty;

    public QuickShellFallback(QuickShellFallbackPage listPage, QuickShellSettingsManager settings)
        : base(BaseCommand, "Saved shortcut", CommandId)
    {
        _ = settings;
        _listPage = listPage;
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

        var shortcuts = QuickShellRuntimeServices.Shortcuts.SearchForRootPalette(_lastQuery).ToArray();
        if (shortcuts.Length == 0)
        {
            ClearResult();
            return;
        }

        _listPage.UpdateSearchText(string.Empty, _lastQuery);
        ApplyListResult(shortcuts);
    }

    private void ApplyListResult(TerminalShortcut[] shortcuts)
    {
        if (shortcuts.Length == 1)
        {
            Title = shortcuts[0].Name;
            Subtitle = ShortcutDisplay.BuildDirectorySubtitle(shortcuts[0]);
        }
        else
        {
            Title = $"{shortcuts.Length} shortcuts";
            Subtitle = $"Matching \"{_lastQuery}\"";
        }

        Icon = new IconInfo("\uE756");
        Command = _listPage;
        MoreCommands = [];
    }

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
