using System.Diagnostics;
using System.Globalization;

namespace QuickShell.Services;

internal static class StartupPerformanceTrace
{
    private const string EnabledEnvironmentVariable = "QUICKSHELL_STARTUP_TRACE";

    public static IDisposable Measure(string name) =>
        IsEnabledValue(Environment.GetEnvironmentVariable(EnabledEnvironmentVariable))
            ? new Measurement(name)
            : NoopDisposable.Instance;

    internal static bool IsEnabledValue(string? value) =>
        value is not null
        && (value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase));

    private sealed class Measurement : IDisposable
    {
        private readonly string _name;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private bool _disposed;

        public Measurement(string name)
        {
            _name = name;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopwatch.Stop();
            Trace.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"QuickShell startup: {_name} {_stopwatch.Elapsed.TotalMilliseconds:0.###}ms"));
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
