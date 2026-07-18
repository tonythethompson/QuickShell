using System.Threading;

namespace QuickShell.Services;

/// <summary>
/// One unit of optional startup work run by <see cref="IStartupWarmupCoordinator"/>.
/// </summary>
internal interface IStartupWarmupStage
{
    string Name { get; }

    void Execute(IStartupWarmupContext context, CancellationToken cancellationToken);
}
