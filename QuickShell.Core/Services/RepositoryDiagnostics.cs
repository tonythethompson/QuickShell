namespace QuickShell.Services;

/// <summary>
/// Thin diagnostics hook for the storage layer. QuickShell.Core has no project references
/// (shared by QuickShell, QuickShell.Run, and QuickShell.Suggest), so it cannot call the host's
/// SupportDiagnostics logger directly. The host wires <see cref="Sink"/> at startup; until then
/// (or in QuickShell.Run/tests, which may leave it unset) reports are silently dropped.
/// </summary>
internal static class RepositoryDiagnostics
{
    internal static Action<string, string, long?>? Sink { get; set; }

    /// <summary>
    /// Reports a repository diagnostic event to the configured sink and event source.
    /// </summary>
    /// <param name="location">The repository location associated with the event.</param>
    /// <param name="eventName">The name of the diagnostic event.</param>
    /// <param name="elapsedMs">The optional elapsed time in milliseconds.</param>
    internal static void Report(string location, string eventName, long? elapsedMs = null)
    {
        Sink?.Invoke(location, eventName, elapsedMs);
        QuickShellEventSource.Log.WriteRepository(location, eventName, elapsedMs);
    }
}
