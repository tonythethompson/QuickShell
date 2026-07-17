using System.Diagnostics;
using QuickShell.Abstractions;

namespace QuickShell.Core.Tests;

/// <summary>
/// Capturing <see cref="IProcessStarter"/> for launch tests. Never starts a real process.
/// </summary>
internal sealed class FakeProcessStarter : IProcessStarter
{
    public List<ProcessStartInfo> Started { get; } = [];

    /// <summary>When set, decides success per start; otherwise <see cref="Succeed"/> is used.</summary>
    public Func<ProcessStartInfo, bool>? ShouldSucceed { get; set; }

    public bool Succeed { get; set; } = true;

    public bool TryStart(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        Started.Add(startInfo);
        if (ShouldSucceed is { } pred)
        {
            return pred(startInfo);
        }

        return Succeed;
    }
}
