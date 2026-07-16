using System.Globalization;
using System.Text.Json;

namespace QuickShell.Services;

internal static class AgentDebugLog
{
    private const string SessionId = "a49e01";
    private static readonly object Sync = new();
    private static readonly Lazy<string[]> LogPaths = new(BuildLogPaths);

    internal static void Write(string location, string message, object? data = null, string? hypothesisId = null, string runId = "pre-fix")
    {
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["sessionId"] = SessionId,
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["location"] = location,
                ["message"] = message,
                ["runId"] = runId,
            };

            if (data is not null)
            {
                payload["data"] = data;
            }

            if (!string.IsNullOrWhiteSpace(hypothesisId))
            {
                payload["hypothesisId"] = hypothesisId;
            }

            var line = JsonSerializer.Serialize(payload) + Environment.NewLine;

            lock (Sync)
            {
                foreach (var path in LogPaths.Value)
                {
                    try
                    {
                        var directory = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        File.AppendAllText(path, line);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                    {
                        // Best effort; try next path.
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or JsonException or ArgumentException or InvalidOperationException)
        {
            // Never let debug logging crash the app.
        }
    }

    internal static void WriteException(string location, Exception ex, string? hypothesisId = null, string runId = "pre-fix")
    {
        Write(
            location,
            "exception",
            new
            {
                type = ex.GetType().FullName,
                message = ex.Message,
                stack = ex.StackTrace,
                innerType = ex.InnerException?.GetType().FullName,
                innerMessage = ex.InnerException?.Message,
            },
            hypothesisId,
            runId);
    }

    private static string[] BuildLogPaths()
    {
        var paths = new List<string>();

        var workspaceRoot = Environment.GetEnvironmentVariable("QUICKSHELL_WORKSPACE_ROOT");
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            paths.Add(Path.Join(workspaceRoot, "debug-a49e01.log"));
        }

        paths.Add(@"A:\QuickShell\debug-a49e01.log");
        paths.Add(Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuickShell",
            "debug-a49e01.log"));

        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
