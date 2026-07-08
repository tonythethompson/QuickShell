namespace QuickShell.Services;

/// <summary>
/// Parses CmdPal deep-link command IDs into typed descriptors.
/// </summary>
internal interface ICommandIdParser
{
    /// <summary>
    /// Attempts to classify <paramref name="rawId"/> using the same match order as
    /// the historical <c>GetCommandItem</c> chain (OpenLaunch before Open).
    /// </summary>
    bool TryParse(string rawId, out CommandDescriptor descriptor);
}
