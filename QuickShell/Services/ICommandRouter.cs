using Microsoft.CommandPalette.Extensions;

namespace QuickShell.Services;

/// <summary>
/// Resolves CmdPal deep-link IDs into <see cref="CommandItem"/> instances.
/// </summary>
internal interface ICommandRouter
{
    /// <summary>
    /// Returns true when the ID is a known QuickShell deep link (even if the
    /// resulting item is null because the target workspace/launch is missing).
    /// Unknown IDs return false so the provider can fall through to the base.
    /// </summary>
    bool TryHandle(string id, QuickShellPageContext context, out ICommandItem? item);
}
