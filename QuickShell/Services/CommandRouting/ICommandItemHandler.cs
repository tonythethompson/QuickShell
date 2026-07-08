using Microsoft.CommandPalette.Extensions;

namespace QuickShell.Services.CommandRouting;

/// <summary>
/// Creates a CmdPal item for a parsed <see cref="CommandDescriptor"/>.
/// </summary>
internal interface ICommandItemHandler
{
    CommandKind Kind { get; }

    ICommandItem? Create(CommandDescriptor descriptor, CommandItemFactoryContext context);
}
