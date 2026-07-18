using System.Text;
using System.Globalization;
using System.Linq;

namespace QuickShell.Services;

internal enum LaunchDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

internal enum LaunchDiagnosticKind
{
    HealthError,
    HealthWarning,
    TerminalLaunched,
    TerminalLaunchFailed,
    CommandHandoff,
    CommandStatusUnavailable,
    CompanionAppLaunched,
    CompanionAppUnavailable,
    DevServerUrlOpened,
    DevServerUrlUnavailable,
    PartialLaunch,
    ProfileFallback,
    ExternalProcessStarted,
}

internal sealed record LaunchDiagnosticEntry(
    LaunchDiagnosticSeverity Severity,
    LaunchDiagnosticKind Kind,
    string Title,
    string? Detail = null);

internal sealed class LaunchDiagnosticsReport
{
    private readonly List<LaunchDiagnosticEntry> _entries = [];
    private readonly Dictionary<string, int> _processStartCounts = new(StringComparer.OrdinalIgnoreCase);

    public LaunchDiagnosticsReport(string workspaceName, DateTimeOffset createdAt)
    {
        WorkspaceName = workspaceName;
        CreatedAt = createdAt;
    }

    public string WorkspaceName { get; }

    public DateTimeOffset CreatedAt { get; }

    public IReadOnlyList<LaunchDiagnosticEntry> Entries => _entries;

    public IReadOnlyDictionary<string, int> ProcessStartCounts => _processStartCounts;

    public bool HasWarningsOrErrors =>
        _entries.Any(entry => entry.Severity is LaunchDiagnosticSeverity.Warning or LaunchDiagnosticSeverity.Error);

    public void AddInfo(LaunchDiagnosticKind kind, string title, string? detail = null) =>
        _entries.Add(new LaunchDiagnosticEntry(LaunchDiagnosticSeverity.Info, kind, title, detail));

    public void AddWarning(LaunchDiagnosticKind kind, string title, string? detail = null) =>
        _entries.Add(new LaunchDiagnosticEntry(LaunchDiagnosticSeverity.Warning, kind, title, detail));

    public void AddError(LaunchDiagnosticKind kind, string title, string? detail = null) =>
        _entries.Add(new LaunchDiagnosticEntry(LaunchDiagnosticSeverity.Error, kind, title, detail));

    public void RecordProcessStart(string executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return;
        }

        _processStartCounts[executableName] = (_processStartCounts.TryGetValue(executableName, out var existing) ? existing : 0) + 1;
        AddInfo(
            LaunchDiagnosticKind.ExternalProcessStarted,
            $"Started external process: {executableName}",
            $"Total starts for this executable: {_processStartCounts[executableName]}");
    }

    public string ToClipboardText()
    {
        var builder = new StringBuilder();
        builder.Append("Quick Shell launch diagnostics for ");
        builder.AppendLine(WorkspaceName);
        builder.Append("Captured: ");
        builder.AppendLine(CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        builder.AppendLine();

        if (_entries.Count == 0)
        {
            builder.AppendLine("No launch events were recorded.");
            return builder.ToString();
        }

        foreach (var entry in _entries)
        {
            builder.Append('[');
            builder.Append(entry.Severity);
            builder.Append("] ");
            builder.Append(entry.Title);
            if (!string.IsNullOrWhiteSpace(entry.Detail))
            {
                builder.Append(" - ");
                builder.Append(entry.Detail);
            }

            builder.AppendLine();
        }

        if (_processStartCounts.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("External process starts:");
            foreach (var (name, count) in _processStartCounts.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0}: {1}", name, count));
            }
        }

        return builder.ToString();
    }
}
