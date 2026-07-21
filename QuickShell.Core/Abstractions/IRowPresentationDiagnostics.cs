namespace QuickShell.Abstractions;

/// <summary>
/// Named counters for row presentation and enrichment instrumentation. Counter-based so
/// tests can assert cache/enrichment behavior deterministically without log parsing.
/// Provider-scoped: each host/test owns its own counts, so no cross-test reset is needed.
/// </summary>
internal interface IRowPresentationDiagnostics
{
    void Record(string eventName);

    long GetCount(string eventName);
}
