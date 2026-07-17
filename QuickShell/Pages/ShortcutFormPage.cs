using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;
using QuickShell.Services;
using QuickShell.Services.WorkspaceEditor;
using System.Threading;

namespace QuickShell.Pages;

internal partial class ShortcutFormPage : ContentPage, IDisposable
{
    private readonly IQuickShellServices _services;
    private readonly TerminalShortcut? _existing;
    private readonly TerminalShortcut? _createSeed;
    private readonly Action? _onSaved;
    private readonly bool _hasOnSaved;
    private readonly Lock _formSync = new();
    private WorkspaceEditor? _editor;
    private ShortcutForm? _form;
    private bool _formNeedsReset;
    private bool _commandsInitialized;
    private bool _disposed;

    public ShortcutFormPage(
        IQuickShellServices services,
        TerminalShortcut? existing = null,
        Action? onSaved = null,
        TerminalShortcut? createSeed = null)
    {
        _services = services;
        _existing = existing is null ? null : CloneShortcut(existing);
        _createSeed = existing is null ? createSeed : null;
        _onSaved = onSaved;
        _hasOnSaved = onSaved is not null;
        var isCreate = _existing is null;
        Id = isCreate
            ? $"com.quickshell.shortcut-form.create.{Guid.NewGuid():N}"
            : $"com.quickshell.shortcut-form.edit.{_existing!.Id}";
        Icon = new IconInfo("\uE70F");
        Title = isCreate ? "New workspace" : $"Edit {_existing!.Name}";
        Name = isCreate ? "Create" : "Edit";
    }

    public override IContent[] GetContent()
    {
        EnsureFormBuilt();
        EnsureCommandsInitialized();
        return [_form!];
    }

    private void EnsureFormBuilt()
    {
        lock (_formSync)
        {
            if (_disposed)
            {
                return;
            }

            if (_form is null || _formNeedsReset)
            {
                _editor?.Dispose();
                _form?.Dispose();
                _editor = new WorkspaceEditor(_services, _services.Lifetime, _onSaved);
                _editor.ResetForOpen(_existing, _existing is null ? _createSeed : null);
                _form = new ShortcutForm(_editor, _services, MarkFormNeedsReset);
                _formNeedsReset = false;
            }
        }
    }

    private void MarkFormNeedsReset()
    {
        lock (_formSync)
        {
            _formNeedsReset = true;
        }
    }

    public void Dispose()
    {
        lock (_formSync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _form?.Dispose();
            _editor?.Dispose();
            _form = null;
            _editor = null;
        }

        GC.SuppressFinalize(this);
    }

    private static TerminalShortcut CloneShortcut(TerminalShortcut shortcut) => new()
    {
        Id = shortcut.Id,
        Name = shortcut.Name,
        Abbreviation = shortcut.Abbreviation,
        Directory = shortcut.Directory,
        Command = shortcut.Command,
        Terminal = shortcut.Terminal,
        WtProfile = shortcut.WtProfile,
        RunAsAdmin = shortcut.RunAsAdmin,
        IsPinned = shortcut.IsPinned,
        PinOrder = shortcut.PinOrder,
        LastUsedUtc = shortcut.LastUsedUtc,
        Launches = [.. shortcut.Launches.Select(WorkspaceMapper.CloneEntry)],
        CompanionApps = [.. shortcut.CompanionApps.Select(CompanionAppNormalization.CloneEntry)],
        DevServerUrl = shortcut.DevServerUrl,
        RepoUrl = shortcut.RepoUrl,
        OpenCompanionAppOnLaunch = shortcut.OpenCompanionAppOnLaunch,
        OpenDevServerOnLaunch = shortcut.OpenDevServerOnLaunch,
        CompanionAppPath = shortcut.CompanionAppPath,
        CompanionAppArguments = shortcut.CompanionAppArguments,
    };
    private void EnsureCommandsInitialized()
    {
        if (!_hasOnSaved || _commandsInitialized || _onSaved is null)
        {
            return;
        }

        Commands = [.. ShortcutContextCommands.BuildFormUndoRedoCommands(
            () =>
            {
                EnsureFormBuilt();
                return _editor!.TryUndo();
            },
            () =>
            {
                EnsureFormBuilt();
                return _editor!.TryRedo();
            },
            _onSaved,
            _services)];
        _commandsInitialized = true;
    }
}
