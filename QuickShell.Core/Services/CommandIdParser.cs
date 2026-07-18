namespace QuickShell.Services;

/// <summary>
/// Default command-ID parser for CmdPal deep links.
/// Delegates all parsing to <see cref="CommandDescriptor.Parser"/> so the ID schema is owned
/// by the descriptor and the parser is a thin adapter.
/// </summary>
internal sealed class CommandIdParser : ICommandIdParser
{
    public bool TryParse(string rawId, out CommandDescriptor descriptor) =>
        CommandDescriptor.Parser.TryParse(rawId, out descriptor);
}
