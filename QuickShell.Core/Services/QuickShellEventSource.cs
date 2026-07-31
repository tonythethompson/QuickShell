using System.Diagnostics.Tracing;
using QuickShell.Abstractions;

namespace QuickShell.Services;

/// <summary>
/// ETW provider for Quick Shell diagnostics. Safe when no listeners are attached:
/// <see cref="EventSource.WriteEvent"/> is a no-op without enabled listeners.
/// </summary>
[EventSource(Name = "QuickShell-Diagnostics")]
internal sealed class QuickShellEventSource : EventSource, IQuickShellEventSource
{
    /// <summary>Process-wide provider instance used by Core and the CmdPal host.</summary>
    public static readonly QuickShellEventSource Log = new();

    private QuickShellEventSource()
    {
    }

    /// <summary>
    /// Determines whether the event source is enabled for listeners.
    /// </summary>
    /// <returns><c>true</c> if the event source is enabled; <c>false</c> otherwise.</returns>
    bool IQuickShellEventSource.IsEnabled() => IsEnabled();

    /// <summary>
    /// Records a row cache diagnostic event.
    /// </summary>
    /// <param name="kind">The row cache category.</param>
    [NonEvent]
    public void WriteRowCache(string kind) => RowCache(kind ?? string.Empty);

    /// <summary>
    /// Records a plan cache diagnostic event.
    /// </summary>
    /// <param name="kind">The plan cache category.</param>
    [NonEvent]
    public void WritePlanCache(string kind) => PlanCache(kind ?? string.Empty);

    /// <summary>
    /// Records the elapsed time for a startup span.
    /// </summary>
    /// <param name="name">The name of the startup span.</param>
    /// <param name="elapsedMs">The elapsed time in milliseconds.</param>
    [NonEvent]
    public void WriteStartupSpan(string name, double elapsedMs) =>
    StartupSpan(name ?? string.Empty, elapsedMs);

    /// <summary>
    /// Records a repository diagnostic event.
    /// </summary>
    /// <param name="location">The repository location.</param>
    /// <param name="eventName">The name of the event.</param>
    /// <param name="elapsedMs">The elapsed time in milliseconds, or null when unavailable.</param>
    [NonEvent]
    public void WriteRepository(string location, string eventName, long? elapsedMs = null) =>
    Repository(location ?? string.Empty, eventName ?? string.Empty, elapsedMs ?? -1);

    /// <summary>
    /// Records a support event using the specified event code.
    /// </summary>
    /// <param name="eventCode">The code identifying the support event.</param>
    [NonEvent]
    public void WriteSupportEvent(string eventCode) => SupportEvent(eventCode ?? string.Empty);

    /// <summary>
    /// Records a support write failure event.
    /// </summary>
    /// <param name="exceptionType">The type of exception associated with the failure.</param>
    [NonEvent]
    public void WriteSupportWriteFailure(string exceptionType) =>
    SupportWriteFailure(exceptionType ?? string.Empty);

    /// <summary>
    /// Records completion of Git repository discovery.
    /// </summary>
    /// <param name="repoCount">The number of repositories discovered.</param>
    [NonEvent]
    public void WriteGitDiscoveryComplete(int repoCount) => GitDiscoveryComplete(repoCount);

    /// <summary>
    /// Records a row cache diagnostic event.
    /// </summary>
    /// <param name="kind">The row cache category.</param>
    [Event(1, Level = EventLevel.Informational, Message = "Row cache {0}")]
    public void RowCache(string kind) => WriteEvent(1, kind);

    /// <summary>
    /// Records a plan cache diagnostic event.
    /// </summary>
    /// <param name="kind">The type of plan cache event.</param>
    [Event(2, Level = EventLevel.Informational, Message = "Plan cache {0}")]
    public void PlanCache(string kind) => WriteEvent(2, kind);

    /// <summary>
    /// Records the duration of a startup operation.
    /// </summary>
    /// <param name="name">The name of the startup operation.</param>
    /// <param name="elapsedMs">The elapsed duration in milliseconds.</param>
    [Event(3, Level = EventLevel.Informational, Message = "Startup {0} {1}ms")]
    public void StartupSpan(string name, double elapsedMs) => WriteEvent(3, name, elapsedMs);

    /// <summary>
    /// Records the elapsed time for a repository event.
    /// </summary>
    /// <param name="location">The repository location.</param>
    /// <param name="eventName">The name of the repository event.</param>
    /// <param name="elapsedMs">The elapsed time in milliseconds.</param>
    [Event(4, Level = EventLevel.Informational, Message = "Repository {0} {1} elapsedMs={2}")]
    public void Repository(string location, string eventName, long elapsedMs) =>
    WriteEvent(4, location, eventName, elapsedMs);

    /// <summary>
    /// Records a support diagnostic event.
    /// </summary>
    /// <param name="eventCode">The code identifying the support event.</param>
    [Event(5, Level = EventLevel.Informational, Message = "Support {0}")]
    public void SupportEvent(string eventCode) => WriteEvent(5, eventCode);

    /// <summary>
    /// Records a support write failure.
    /// </summary>
    /// <param name="exceptionType">The type of exception associated with the failure.</param>
    [Event(6, Level = EventLevel.Warning, Message = "Support write failure {0}")]
    public void SupportWriteFailure(string exceptionType) => WriteEvent(6, exceptionType);

    /// <summary>
    /// Records the completion of Git repository discovery.
    /// </summary>
    /// <param name="repoCount">The number of repositories discovered.</param>
    [Event(7, Level = EventLevel.Informational, Message = "Git discovery complete repos={0}")]
    public void GitDiscoveryComplete(int repoCount) => WriteEvent(7, repoCount);
}
