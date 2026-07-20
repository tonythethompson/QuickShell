namespace QuickShell.Abstractions;

/// <summary>
/// Structured ETW / EventSource diagnostics for slow paths and cache instrumentation.
/// Payloads must stay redacted: no full user paths, command text, or secrets.
/// </summary>
internal interface IQuickShellEventSource
{
    bool IsEnabled();

    void WriteRowCache(string kind);

    void WritePlanCache(string kind);

    void WriteStartupSpan(string name, double elapsedMs);

    void WriteRepository(string location, string eventName, long? elapsedMs = null);

    void WriteSupportEvent(string eventCode);

    void WriteSupportWriteFailure(string exceptionType);

    void WriteGitDiscoveryComplete(int repoCount);
}
