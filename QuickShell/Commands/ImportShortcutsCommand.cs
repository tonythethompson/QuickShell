using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;
using System.Threading;

namespace QuickShell.Commands;

internal sealed partial class ImportShortcutsCommand : InvokableCommand
{
    private static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(30);
    private readonly IQuickShellServices _services;
    private readonly Action _onReload;
    private readonly Action? _onSettingsRefresh;
    private readonly bool _stayOnSettings;

    public ImportShortcutsCommand(
        Action onReload,
        bool stayOnSettings,
        Action? onSettingsRefresh,
        IQuickShellServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _onReload = onReload;
        _stayOnSettings = stayOnSettings;
        _onSettingsRefresh = onSettingsRefresh;
        Name = Strings.Command_ImportWorkspaces_Name;
        Icon = new IconInfo("\uE898");
    }

    public override CommandResult Invoke()
    {
        var path = ShortcutFilePickerService.PickImportFile(_services);
        if (path is null)
        {
            return Finish(Strings.Import_Cancelled);
        }

        using var readCancellation = new CancellationTokenSource(IoTimeout);
        var readResult = _services.Shortcuts.TryReadImportFileAsync(path, readCancellation.Token).GetAwaiter().GetResult();
        if (!readResult.Success)
        {
            return Finish(readResult.Error, isError: true);
        }

        var imported = readResult.Shortcuts;
        var conflicts = _services.Shortcuts.CountImportNameConflicts(imported);
        if (conflicts > 0)
        {
            ImportConflictState.Set(ImportTransferKind.Projects, path, conflicts, imported.Length, _onReload);
            _services.RefreshScheduler.ScheduleRefresh(_onSettingsRefresh);
            return _stayOnSettings
                ? QuickShellNavigation.StayOnSettings()
                : QuickShellNavigation.GoToSettings();
        }

        using var mergeCancellation = new CancellationTokenSource(IoTimeout);
        var result = _services.Shortcuts.ImportMergeAsync(path, mergeCancellation.Token).GetAwaiter().GetResult();
        if (!result.Success)
        {
            return Finish(result.Message, isError: true);
        }

        _onReload();
        _services.RefreshScheduler.ScheduleRefresh(_onSettingsRefresh);
        return Finish(result.Message);
    }

    private CommandResult Finish(string? message, bool isError = false, bool navigateToSettings = false)
    {
        if (navigateToSettings)
        {
            return QuickShellNavigation.GoToSettings(message);
        }

        return _stayOnSettings
            ? QuickShellNavigation.StayOnSettings(message)
            : QuickShellNavigation.StayOpen(message);
    }
}
