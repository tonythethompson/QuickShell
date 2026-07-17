using QuickShell.Abstractions;

namespace QuickShell.Services;

/// <summary>
/// Default scheduler for tests and hosts without a UI thread — runs callbacks inline.
/// </summary>
internal sealed class SyncExtensionThreadScheduler : IExtensionThreadScheduler
{
    public void Post(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        callback();
    }
}
