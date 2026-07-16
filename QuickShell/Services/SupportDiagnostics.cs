using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickShell.Services;

internal enum SupportLogSeverity
{
    Info,
    Warning,
    Error,
}

internal static class SupportDiagnostics
{
    private const int DefaultMaximumLogFileBytes = 512 * 1024;
    private const int MaximumLogFileCount = 3;
    private const string ActiveLogFileName = "support.jsonl";
    private static readonly object Gate = new();

    internal static string? LogDirectoryOverride { get; set; }

    internal static int? MaximumLogFileBytesOverride { get; set; }

    internal static void WriteInfo(string eventCode) => WriteEvent(SupportLogSeverity.Info, eventCode);

    internal static void WriteWarning(string eventCode) => WriteEvent(SupportLogSeverity.Warning, eventCode);

    internal static void WriteError(string eventCode, Exception exception) =>
        WriteEvent(SupportLogSeverity.Error, eventCode, exception);

    internal static void Write(
        string location,
        string message,
        object? data = null,
        string? hypothesisId = null,
        string? runId = null) =>
        WriteInfo(NormalizeEventCode(location));

    internal static void WriteException(
        string location,
        Exception exception,
        string? hypothesisId = null,
        string? runId = null) =>
        WriteError(NormalizeEventCode(location), exception);

    internal static void WriteEvent(SupportLogSeverity severity, string eventCode, Exception? exception = null)
    {
        if (string.IsNullOrWhiteSpace(eventCode))
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                var directory = GetLogDirectory();
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, ActiveLogFileName);
                var json = JsonSerializer.Serialize(new SupportLogEvent(
                    DateTimeOffset.UtcNow,
                    eventCode,
                    severity.ToString(),
                    exception?.GetType().Name,
                    exception?.HResult), SupportDiagnosticsJsonContext.Default.SupportLogEvent);
                var line = json + Environment.NewLine;

                if (File.Exists(path)
                    && new FileInfo(path).Length + Encoding.UTF8.GetByteCount(line) > GetMaximumLogFileBytes())
                {
                    RotateLogs(directory, path);
                }

                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Support diagnostics must never interfere with the extension host.
        }
    }

    internal static string BuildBundle(LaunchDiagnosticsReport? diagnostics)
    {
        var aggregate = diagnostics is null
            ? null
            : new LaunchDiagnosticsAggregate(
                diagnostics.CreatedAt,
                diagnostics.Entries
                    .GroupBy(entry => entry.Severity.ToString())
                    .ToDictionary(group => group.Key, group => group.Count()),
                diagnostics.Entries
                    .GroupBy(entry => entry.Kind.ToString())
                    .ToDictionary(group => group.Key, group => group.Count()));
        var bundle = new SupportBundle(
            SchemaVersion: 1,
            ApplicationVersion: Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
            OperatingSystem: RuntimeInformation.OSDescription,
            LogDirectory: GetLogDirectory(),
            LaunchDiagnostics: aggregate);
        return JsonSerializer.Serialize(bundle, SupportDiagnosticsJsonContext.Default.SupportBundle);
    }

    internal static bool TryCopyBundle(LaunchDiagnosticsReport? diagnostics, out string message)
    {
        if (!StaClipboard.TrySetText(BuildBundle(diagnostics)))
        {
            message = Strings.Diagnostics_CopySupportBundleFailed;
            return false;
        }

        message = Strings.Diagnostics_SupportBundleCopied;
        return true;
    }

    internal static bool TryOpenLogFolder(out string error)
    {
        try
        {
            var directory = GetLogDirectory();
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
            error = string.Empty;
            return true;
        }
        catch
        {
            error = Strings.Diagnostics_LogFolderOpenFailed;
            return false;
        }
    }

    internal static void ResetForTests()
    {
        LogDirectoryOverride = null;
        MaximumLogFileBytesOverride = null;
    }

    private static string GetLogDirectory() =>
        LogDirectoryOverride
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickShell", "logs");

    private static int GetMaximumLogFileBytes() =>
        MaximumLogFileBytesOverride is > 0 ? MaximumLogFileBytesOverride.Value : DefaultMaximumLogFileBytes;

    private static void RotateLogs(string directory, string activePath)
    {
        var oldest = Path.Combine(directory, $"support.{MaximumLogFileCount - 1}.jsonl");
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var index = MaximumLogFileCount - 2; index >= 1; index--)
        {
            var source = Path.Combine(directory, $"support.{index}.jsonl");
            if (File.Exists(source))
            {
                File.Move(source, Path.Combine(directory, $"support.{index + 1}.jsonl"));
            }
        }

        File.Move(activePath, Path.Combine(directory, "support.1.jsonl"));
    }

    private static string NormalizeEventCode(string location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return "host.unknown";
        }

        var characters = location
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '.')
            .ToArray();
        return "host." + new string(characters).Trim('.');
    }

}

internal sealed record SupportLogEvent(
    DateTimeOffset TimestampUtc,
    string EventCode,
    string Severity,
    string? ExceptionType,
    int? HResult);

internal sealed record LaunchDiagnosticsAggregate(
    DateTimeOffset CapturedAtUtc,
    IReadOnlyDictionary<string, int> CountsBySeverity,
    IReadOnlyDictionary<string, int> CountsByKind);

internal sealed record SupportBundle(
    int SchemaVersion,
    string ApplicationVersion,
    string OperatingSystem,
    string LogDirectory,
    LaunchDiagnosticsAggregate? LaunchDiagnostics);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SupportLogEvent))]
[JsonSerializable(typeof(SupportBundle))]
internal sealed partial class SupportDiagnosticsJsonContext : JsonSerializerContext;
