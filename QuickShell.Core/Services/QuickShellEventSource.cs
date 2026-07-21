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

    bool IQuickShellEventSource.IsEnabled() => IsEnabled();

    [NonEvent]
    public void WriteRowCache(string kind) => RowCache(kind ?? string.Empty);

    [NonEvent]
    public void WritePlanCache(string kind) => PlanCache(kind ?? string.Empty);

    [NonEvent]
    public void WriteStartupSpan(string name, double elapsedMs) =>
        StartupSpan(name ?? string.Empty, elapsedMs);

    [NonEvent]
    public void WriteRepository(string location, string eventName, long? elapsedMs = null) =>
        Repository(location ?? string.Empty, eventName ?? string.Empty, elapsedMs ?? -1);

    [NonEvent]
    public void WriteSupportEvent(string eventCode) => SupportEvent(eventCode ?? string.Empty);

    [NonEvent]
    public void WriteSupportWriteFailure(string exceptionType) =>
        SupportWriteFailure(exceptionType ?? string.Empty);

    [NonEvent]
    public void WriteGitDiscoveryComplete(int repoCount) => GitDiscoveryComplete(repoCount);

    // [Event] methods must be public instance methods that call WriteEvent for ETW to enable them.
    [Event(1, Level = EventLevel.Informational, Message = "Row cache {0}")]
    public void RowCache(string kind) => WriteEvent(1, kind);

    [Event(2, Level = EventLevel.Informational, Message = "Plan cache {0}")]
    public void PlanCache(string kind) => WriteEvent(2, kind);

    [Event(3, Level = EventLevel.Informational, Message = "Startup {0} {1}ms")]
    public void StartupSpan(string name, double elapsedMs) => WriteEvent(3, name, elapsedMs);

    [Event(4, Level = EventLevel.Informational, Message = "Repository {0} {1} elapsedMs={2}")]
    public void Repository(string location, string eventName, long elapsedMs) =>
        WriteEvent(4, location, eventName, elapsedMs);

    [Event(5, Level = EventLevel.Informational, Message = "Support {0}")]
    public void SupportEvent(string eventCode) => WriteEvent(5, eventCode);

    [Event(6, Level = EventLevel.Warning, Message = "Support write failure {0}")]
    public void SupportWriteFailure(string exceptionType) => WriteEvent(6, exceptionType);

    [Event(7, Level = EventLevel.Informational, Message = "Git discovery complete repos={0}")]
    public void GitDiscoveryComplete(int repoCount) => WriteEvent(7, repoCount);
}
