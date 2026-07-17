using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
        WriteEvent(
            SupportLogSeverity.Info,
            NormalizeEventCode(location),
            tags: BuildRedactedTags(message, data, hypothesisId, runId));

    internal static void WriteException(
        string location,
        Exception exception,
        string? hypothesisId = null,
        string? runId = null) =>
        WriteEvent(
            SupportLogSeverity.Error,
            NormalizeEventCode(location),
            exception,
            BuildRedactedTags(message: null, data: null, hypothesisId, runId));

    internal static void WriteEvent(
        SupportLogSeverity severity,
        string eventCode,
        Exception? exception = null,
        IReadOnlyList<string>? tags = null)
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
                var path = Path.Join(directory, ActiveLogFileName);
                var json = JsonSerializer.Serialize(new SupportLogEvent(
                    DateTimeOffset.UtcNow,
                    eventCode,
                    severity.ToString(),
                    exception?.GetType().Name,
                    exception?.HResult,
                    tags), SupportDiagnosticsJsonContext.Default.SupportLogEvent);
                var line = json + Environment.NewLine;

                if (File.Exists(path)
                    && new FileInfo(path).Length + Encoding.UTF8.GetByteCount(line) > GetMaximumLogFileBytes())
                {
                    RotateLogs(directory, path);
                }

                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }
        catch (Exception ex) when (
            ex is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or System.Security.SecurityException
            or JsonException)
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
        catch (UnauthorizedAccessException)
        {
            error = Strings.Diagnostics_LogFolderOpenFailed;
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            error = Strings.Diagnostics_LogFolderOpenFailed;
            return false;
        }
        catch (IOException)
        {
            error = Strings.Diagnostics_LogFolderOpenFailed;
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            error = Strings.Diagnostics_LogFolderOpenFailed;
            return false;
        }
        catch (InvalidOperationException)
        {
            error = Strings.Diagnostics_LogFolderOpenFailed;
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            error = Strings.Diagnostics_LogFolderOpenFailed;
            return false;
        }
        catch (ArgumentException)
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
        ?? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickShell", "logs");

    private static int GetMaximumLogFileBytes() =>
        MaximumLogFileBytesOverride is > 0 ? MaximumLogFileBytesOverride.Value : DefaultMaximumLogFileBytes;

    private static void RotateLogs(string directory, string activePath)
    {
        var oldest = Path.Join(directory, $"support.{MaximumLogFileCount - 1}.jsonl");
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var index = MaximumLogFileCount - 2; index >= 1; index--)
        {
            var source = Path.Join(directory, $"support.{index}.jsonl");
            if (File.Exists(source))
            {
                File.Move(source, Path.Join(directory, $"support.{index + 1}.jsonl"));
            }
        }

        File.Move(activePath, Path.Join(directory, "support.1.jsonl"));
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

    private static List<string>? BuildRedactedTags(
        string? message,
        object? data,
        string? hypothesisId,
        string? runId)
    {
        List<string>? tags = null;
        AddHashTag(ref tags, "message", message);
        if (data is not null)
        {
            (tags ??= []).Add("data:present");
        }

        AddBoundedTag(ref tags, "hypothesis", hypothesisId);
        AddBoundedTag(ref tags, "run", runId);
        return tags;
    }

    private static void AddHashTag(ref List<string>? tags, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        (tags ??= []).Add($"{name}:sha256:{HashToken(value)}");
    }

    private static void AddBoundedTag(ref List<string>? tags, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmed = value.Trim();
        var tagValue = IsSafeTagValue(trimmed)
            ? trimmed.ToLowerInvariant()
            : $"sha256:{HashToken(trimmed)}";
        (tags ??= []).Add($"{name}:{tagValue}");
    }

    private static bool IsSafeTagValue(string value) =>
        value.Length is > 0 and <= 32
        && value.All(character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '-' or '_' or '.');

    private static string HashToken(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
    }
}

internal sealed record SupportLogEvent(
    DateTimeOffset TimestampUtc,
    string EventCode,
    string Severity,
    string? ExceptionType,
    int? HResult,
    IReadOnlyList<string>? Tags);

internal sealed record LaunchDiagnosticsAggregate(
    DateTimeOffset CapturedAtUtc,
    IReadOnlyDictionary<string, int> CountsBySeverity,
    IReadOnlyDictionary<string, int> CountsByKind);

internal sealed record SupportBundle(
    int SchemaVersion,
    string ApplicationVersion,
    string OperatingSystem,
    LaunchDiagnosticsAggregate? LaunchDiagnostics);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SupportLogEvent))]
[JsonSerializable(typeof(SupportBundle))]
internal sealed partial class SupportDiagnosticsJsonContext : JsonSerializerContext;
