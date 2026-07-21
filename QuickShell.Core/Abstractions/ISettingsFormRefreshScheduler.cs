namespace QuickShell.Abstractions;

/// <summary>
/// Defers settings-form UI refresh so CmdPal can show a page-level toast (or complete
/// navigation) before the heavier refresh work runs.
/// </summary>
internal interface ISettingsFormRefreshScheduler
{
    /// <summary>
    /// Defers the refresh so the calling page can return its current items before the
    /// heavier refresh work runs, then marshals the callback back to the CmdPal extension
    /// thread (drained from GetItems) so RaiseItemsChanged and page notifications run
    /// where the host expects them. COM/disposed exceptions are swallowed by the queue
    /// drain, matching <see cref="ScheduleRefresh"/>.
    /// </summary>
    void SchedulePostNavigationRefresh(Action? refresh);

    /// <summary>Defers settings UI refresh so CmdPal can show a page-level toast first.</summary>
    void ScheduleRefresh(Action? refresh, int delayMs = 50);
}
