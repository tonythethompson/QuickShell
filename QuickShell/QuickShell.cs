using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CommandPalette.Extensions;
using QuickShell.Services;

namespace QuickShell;

[Guid("528cc766-cbe8-4861-9933-722c7a3f3581")]
public sealed partial class QuickShellExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _extensionDisposedEvent;
    private readonly QuickShellCommandsProvider _provider = new();
    private readonly PackageServicingShutdownWatcher _packageShutdown;

    public QuickShellExtension(ManualResetEvent extensionDisposedEvent)
    {
        _extensionDisposedEvent = extensionDisposedEvent;
        _packageShutdown = PackageServicingShutdownWatcher.Start(extensionDisposedEvent);
    }

    public object? GetProvider(ProviderType providerType) => providerType switch
    {
        ProviderType.Commands => _provider,
        _ => null,
    };

    public void Dispose()
    {
        _provider.Dispose();
        _packageShutdown.Dispose();
        _extensionDisposedEvent.Set();
    }
}
