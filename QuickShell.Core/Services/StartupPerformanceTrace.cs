using System.Diagnostics;
using System.Globalization;

namespace QuickShell.Services;

internal static class StartupPerformanceTrace
{
    private const string EnabledEnvironmentVariable = "QUICKSHELL_STARTUP_TRACE";

    public static IDisposable Measure(string name)
    {
        var writeTrace = IsEnabledValue(Environment.GetEnvironmentVariable(EnabledEnvironmentVariable));
        if (!writeTrace && !QuickShellEventSource.Log.IsEnabled())
        {
            return NoopDisposable.Instance;
        }

        return new Measurement(name, writeTrace);
    }

    public static void Write(string message)
    {
        if (IsEnabledValue(Environment.GetEnvironmentVariable(EnabledEnvironmentVariable)))
        {
            Trace.WriteLine($"QuickShell startup: {message}");
        }
    }

    internal static bool IsEnabledValue(string? value) =>
        value is not null
        && (value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase));

    private sealed class Measurement : IDisposable
    {
        private readonly string _name;
        private readonly bool _writeTrace;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private bool _disposed;

        public Measurement(string name, bool writeTrace)
        {
            _name = name;
            _writeTrace = writeTrace;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopwatch.Stop();
            var elapsedMs = _stopwatch.Elapsed.TotalMilliseconds;
            QuickShellEventSource.Log.WriteStartupSpan(_name, elapsedMs);
            if (!_writeTrace)
            {
                return;
            }

            Trace.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"QuickShell startup: {_name} {elapsedMs:0.###}ms"));
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
