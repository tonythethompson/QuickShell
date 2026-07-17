namespace QuickShell.Abstractions;

/// <summary>
/// Marshals work onto the host UI/extension thread when one exists.
/// Hosts without a synchronization context (tests, Run) run callbacks inline.
/// </summary>
internal interface IExtensionThreadScheduler
{
    void Post(Action callback);
}
