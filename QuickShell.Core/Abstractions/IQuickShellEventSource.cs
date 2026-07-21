namespace QuickShell.Abstractions;

/// <summary>
/// Structured ETW / EventSource diagnostics for slow paths and cache instrumentation.
/// Payloads must stay redacted: no full user paths, command text, or secrets.
/// </summary>
internal interface IQuickShellEventSource
{
    /// <summary>
/// Determines whether diagnostic event writing is enabled.
/// </summary>
/// <returns><c>true</c> if diagnostic event writing is enabled; otherwise, <c>false</c>.</returns>
bool IsEnabled();

    /// <summary>
/// Emits a diagnostic event for row-cache activity.
/// </summary>
/// <param name="kind">The kind of row-cache activity.</param>
void WriteRowCache(string kind);

    /// <summary>
/// Emits a diagnostic event for plan-cache activity.
/// </summary>
/// <param name="kind">The type of plan-cache activity.</param>
void WritePlanCache(string kind);

    /// <summary>
/// Records the duration of a startup operation.
/// </summary>
/// <param name="name">The name identifying the startup operation.</param>
/// <param name="elapsedMs">The operation duration in milliseconds.</param>
void WriteStartupSpan(string name, double elapsedMs);

    /// <summary>
/// Records a repository-related diagnostic event.
/// </summary>
/// <param name="location">The repository location associated with the event.</param>
/// <param name="eventName">The name of the repository event.</param>
/// <param name="elapsedMs">The optional elapsed time in milliseconds.</param>
void WriteRepository(string location, string eventName, long? elapsedMs = null);

    /// <summary>
/// Emits a support diagnostic event identified by an event code.
/// </summary>
/// <param name="eventCode">The identifier for the support diagnostic event.</param>
void WriteSupportEvent(string eventCode);

    /// <summary>
/// Records a diagnostic event for a failure to write support diagnostics.
/// </summary>
/// <param name="exceptionType">The type of exception that caused the write failure.</param>
void WriteSupportWriteFailure(string exceptionType);

    /// <summary>
/// Records the completion of Git repository discovery.
/// </summary>
/// <param name="repoCount">The number of repositories discovered.</param>
void WriteGitDiscoveryComplete(int repoCount);
}
