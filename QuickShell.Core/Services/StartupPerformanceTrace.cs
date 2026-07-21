using System.Diagnostics;
using System.Globalization;

namespace QuickShell.Services;

internal static class StartupPerformanceTrace
{
    private const string EnabledEnvironmentVariable = "QUICKSHELL_STARTUP_TRACE";

    /// <summary>
    /// Measures a startup operation and records its elapsed duration when startup tracing or event-source logging is enabled.
    /// </summary>
    /// <param name="name">The label associated with the startup operation.</param>
    /// <returns>A disposable measurement that records the elapsed duration when disposed.</returns>
    public static IDisposable Measure(string name)
    {
        var writeTrace = IsEnabledValue(Environment.GetEnvironmentVariable(EnabledEnvironmentVariable));
        if (!writeTrace && !QuickShellEventSource.Log.IsEnabled())
        {
            return NoopDisposable.Instance;
        }

        return new Measurement(name, writeTrace);
    }

    /// <summary>
    /// Writes a startup trace message when startup tracing is enabled.
    /// </summary>
    /// <param name="message">The message to write.</param>
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

        /// <summary>
        /// Initializes a startup performance measurement.
        /// </summary>
        /// <param name="name">The label associated with the measurement.</param>
        /// <param name="writeTrace">Whether to write the measurement result to the trace output.</param>
        public Measurement(string name, bool writeTrace)
        {
            _name = name;
            _writeTrace = writeTrace;
        }

        /// <summary>
        /// Records the elapsed startup duration and optionally writes it to the trace output.
        /// </summary>
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
