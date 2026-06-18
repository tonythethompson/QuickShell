using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using TerminalShortcuts.Commands;
using TerminalShortcuts.Pages;
using TerminalShortcuts.Services;

namespace TerminalShortcuts;

public partial class TerminalShortcutsCommandsProvider : CommandProvider
{
    private readonly TerminalShortcutsPage _page;
    private readonly ICommandItem[] _commands;
    private readonly IFallbackCommandItem[] _fallbacks;

    public TerminalShortcutsCommandsProvider()
    {
        DisplayName = "Terminal Shortcuts";
        Icon = new IconInfo("\uE756");
        _page = new TerminalShortcutsPage();
        _commands = [new CommandItem(_page) { Title = DisplayName, Subtitle = "Open saved terminal directories and commands" }];
        _fallbacks = [new TerminalShortcutsFallback(_page)];
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override IFallbackCommandItem[] FallbackCommands() => _fallbacks;
}
