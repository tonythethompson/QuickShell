using QuickShell.Commands;
using QuickShell.Services;

namespace QuickShell.Services.CommandRouting;

/// <summary>
/// Shared dependencies for deep-link command item handlers.
/// </summary>
internal sealed class CommandItemFactoryContext
{
    public required IQuickShellServices Services { get; init; }

    public required IShortcutRepository Shortcuts { get; init; }

    public required QuickShellSettingsManager Settings { get; init; }

    public required CreateShortcutCommand CreateShortcut { get; init; }

    public required Action ReloadPages { get; init; }
}
