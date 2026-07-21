using System.Collections.Concurrent;
using System.Diagnostics;
using QuickShell.Abstractions;

namespace QuickShell.Core.Tests;

/// <summary>
/// Capturing <see cref="IProcessStarter"/> for launch tests. Never starts a real process.
/// </summary>
internal sealed class FakeProcessStarter : IProcessStarter
{
    private readonly ConcurrentQueue<ProcessStartInfo> _started = new();

    public IReadOnlyList<ProcessStartInfo> Started => _started.ToArray();

    /// <summary>When set, decides success per start; otherwise <see cref="Succeed"/> is used.</summary>
    public Func<ProcessStartInfo, bool>? ShouldSucceed { get; set; }

    public bool Succeed { get; set; } = true;

    public bool TryStart(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        _started.Enqueue(startInfo);
        if (ShouldSucceed is { } pred)
        {
            return pred(startInfo);
        }

        return Succeed;
    }
}
