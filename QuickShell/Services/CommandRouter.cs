using Microsoft.CommandPalette.Extensions;
using QuickShell.Services.CommandRouting;

namespace QuickShell.Services;

/// <summary>
/// Resolves CmdPal deep-link IDs into <see cref="CommandItem"/> instances via registered handlers.
/// </summary>
internal sealed class CommandRouter : ICommandRouter
{
    private readonly ICommandIdParser _parser;
    private readonly CommandItemFactoryContext _context;
    private readonly Dictionary<CommandKind, ICommandItemHandler> _handlers;

    public CommandRouter(
        ICommandIdParser parser,
        CommandItemFactoryContext context,
        IEnumerable<ICommandItemHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(handlers);

        _parser = parser;
        _context = context;
        _handlers = handlers.ToDictionary(handler => handler.Kind);
    }

    public bool TryHandle(string id, out ICommandItem? item)
    {
        item = null;

        if (!_parser.TryParse(id, out var descriptor))
        {
            return false;
        }

        if (!_handlers.TryGetValue(descriptor.Kind, out var handler))
        {
            return true;
        }

        item = handler.Create(descriptor, _context);
        return true;
    }
}
