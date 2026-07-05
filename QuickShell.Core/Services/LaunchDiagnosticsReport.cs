using System.Text;
using System.Globalization;

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
}

internal sealed record LaunchDiagnosticEntry(
    LaunchDiagnosticSeverity Severity,
    LaunchDiagnosticKind Kind,
    string Title,
    string? Detail = null);

internal sealed class LaunchDiagnosticsReport
{
    private readonly List<LaunchDiagnosticEntry> _entries = [];

    public LaunchDiagnosticsReport(string workspaceName, DateTimeOffset createdAt)
    {
        WorkspaceName = workspaceName;
        CreatedAt = createdAt;
    }

    public string WorkspaceName { get; }

    public DateTimeOffset CreatedAt { get; }

    public IReadOnlyList<LaunchDiagnosticEntry> Entries => _entries;

    public bool HasWarningsOrErrors =>
        _entries.Any(entry => entry.Severity is LaunchDiagnosticSeverity.Warning or LaunchDiagnosticSeverity.Error);

    public void AddInfo(LaunchDiagnosticKind kind, string title, string? detail = null) =>
        _entries.Add(new LaunchDiagnosticEntry(LaunchDiagnosticSeverity.Info, kind, title, detail));

    public void AddWarning(LaunchDiagnosticKind kind, string title, string? detail = null) =>
        _entries.Add(new LaunchDiagnosticEntry(LaunchDiagnosticSeverity.Warning, kind, title, detail));

    public void AddError(LaunchDiagnosticKind kind, string title, string? detail = null) =>
        _entries.Add(new LaunchDiagnosticEntry(LaunchDiagnosticSeverity.Error, kind, title, detail));

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

        return builder.ToString();
    }
}
