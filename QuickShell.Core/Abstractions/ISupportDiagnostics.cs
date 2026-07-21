using QuickShell.Services;

namespace QuickShell.Abstractions;

/// <summary>
/// Bounded, redacted JSONL support logging plus an aggregate support bundle. Implemented
/// by the host so pages/commands can log without depending on file-system details.
/// </summary>
internal interface ISupportDiagnostics
{
    void WriteInfo(string eventCode);

    void WriteWarning(string eventCode);

    void WriteError(string eventCode, Exception exception);

    void Write(string location, string message, object? data = null);

    void WriteException(string location, Exception exception);

    string BuildBundle(LaunchDiagnosticsReport? diagnostics);

    bool TryCopyBundle(LaunchDiagnosticsReport? diagnostics, out string message);

    bool TryOpenLogFolder(out string error);
}
