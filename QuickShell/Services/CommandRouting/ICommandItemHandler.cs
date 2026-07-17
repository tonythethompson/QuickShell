using Microsoft.CommandPalette.Extensions;
using QuickShell.Services;

namespace QuickShell.Services.CommandRouting;

/// <summary>
/// Creates a CmdPal item for a parsed <see cref="CommandDescriptor"/>.
/// </summary>
internal interface ICommandItemHandler
{
    CommandKind Kind { get; }

    ICommandItem? Create(CommandDescriptor descriptor, QuickShellPageContext context);
}
