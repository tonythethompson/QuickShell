using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using QuickShell.Abstractions;
using QuickShell.Models;
using QuickShell.Pages;

namespace QuickShell.Services.WorkspaceEditor;

internal sealed partial class WorkspaceEditor(IQuickShellServices services, IQuickShellLifetime lifetime, Action? onSaved = null) : IWorkspaceEditor
{
    private readonly IQuickShellServices _services = services ?? throw new ArgumentNullException(nameof(services));
    private readonly IQuickShellLifetime _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
    private readonly Action? _onSaved = onSaved;
    private readonly Lock _sync = new();
    private readonly FormEditHistory<FormEditSnapshot> _editHistory = new(snapshot => snapshot.Clone());

    private CancellationTokenSource? _scanCts;
    private int _scanGeneration;
    private bool _disposed;
    private bool _baselineReady;
    private bool _suggestionScanComplete;
    private string? _originalName;
    private string? _autoFilledName;
    private bool _nameCustomized;
    private bool _showRestoredDraftNote;
    private bool _subscribedToDraftCleared;
    private Action<string>? _draftClearedHandler;
    private string _saveError = string.Empty;
    private FormDraft _draft = new();
    private FormDraft _baselineDraft = new();

    public WorkspaceEditState GetState()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return BuildState();
        }
    }

    public bool CanUndo
    {
        get { lock (_sync) { return _editHistory.CanUndo; } }
    }

    public bool CanRedo
    {
        get { lock (_sync) { return _editHistory.CanRedo; } }
    }

    public bool HasUnsavedChanges
    {
        get { lock (_sync) { return _baselineReady && !DraftEquals(_draft, _baselineDraft); } }
    }

    public bool IsSuggestionScanning
    {
        get { lock (_sync) { return !_suggestionScanComplete && !string.IsNullOrWhiteSpace(_draft.Directory); } }
    }

    public event EventHandler<WorkspaceEditChangedEventArgs>? Changed;

    public void ResetForOpen(TerminalShortcut? existing, TerminalShortcut? createSeed)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            UnsubscribeFromDraftCleared();
            CancelScan();
            _scanCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.CancellationToken, CancellationToken.None);
            _saveError = string.Empty;
            _showRestoredDraftNote = false;
            _nameCustomized = false;
            _autoFilledName = null;
            _editHistory.Clear();
            _baselineReady = false;
            _suggestionScanComplete = false;
            InitializeDraft(existing, createSeed);
            _baselineDraft = CloneDraft(_draft);
            _baselineReady = true;
            TryRestoreEditDraft();
            ScheduleSuggestionScan();
        }
    }

    public bool TryApplyInputs(string payload, bool excludeDirectory = false)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_baselineReady)
            {
                return false;
            }

            if (!MergeDraftFromInputs(payload, excludeDirectory))
            {
                return false;
            }

            PersistEditDraftIfNeeded();
            OnChanged();
            return true;
        }
    }

    public WorkspaceEditResult SelectDirectory(string directory)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ApplyDirectorySelection(directory);
            PersistEditDraftIfNeeded();
            OnChanged();
            return WorkspaceEditResult.StayOpen();
        }
    }

    public WorkspaceEditResult TryAddSuggestedCommand(string? command, string? taskType, int pillIndex)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var pills = CommandSuggestionService.GetPills(
                _draft.Directory,
                _draft.Commands.Select(c => c.Command),
                _services.ProjectAnalysis,
                _services.ClassificationCache);

            var pill = CommandSuggestionService.TryFindPill(pills, command, taskType);
            if (pill is null && pillIndex >= 0 && pillIndex < pills.Count)
            {
                pill = pills[pillIndex];
            }

            if (pill is null)
            {
                return WorkspaceEditResult.StayOpen("That suggestion is no longer available.");
            }

            PushEditSnapshot();
            _ = CommandSuggestionService.ApplyPill(_draft.Commands, pill, GetDefaultRowLaunchTarget());
            ApplyDraft();
            return WorkspaceEditResult.StayOpen($"Added {pill.TypeTitle} command.");
        }
    }

    public WorkspaceEditResult ClearLaunchRow(int index)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (index < 0 || index >= _draft.Commands.Count)
            {
                return WorkspaceEditResult.StayOpen();
            }

            PushEditSnapshot();
            LaunchRowListEditor.ClearRow(_draft.Commands, index, GetDefaultRowLaunchTarget());
            ApplyDraft();
            return WorkspaceEditResult.StayOpen();
        }
    }

    public WorkspaceEditResult SetExpandSuggestionPills(bool expand)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            PushEditSnapshot();
            _draft.ExpandSuggestionPills = expand;
            ApplyDraft();
            return WorkspaceEditResult.StayOpen();
        }
    }

    public WorkspaceEditResult RefreshTerminals(IReadOnlyList<string> availableTargetIds, string defaultTargetId)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (var command in _draft.Commands)
            {
                if (!availableTargetIds.Any(t => t.Equals(command.LaunchTarget, StringComparison.OrdinalIgnoreCase)))
                {
                    command.LaunchTarget = defaultTargetId;
                }
            }

            SyncDraftLaunchTargetFromCommands();
            SyncDraftRunAsAdminFromCommands();
            ApplyDraft();
            return WorkspaceEditResult.StayOpen(Strings.RefreshTerminals_Toast);
        }
    }

    public WorkspaceEditResult AddCompanionRow()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!CompanionAppFormEditor.CanAdd(_draft.Companions))
            {
                _saveError = $"At most {CompanionAppFormEditor.MaxCount} companion apps are supported.";
                OnChanged();
                return WorkspaceEditResult.StayOpen(_saveError);
            }

            PushEditSnapshot();
            CompanionAppFormEditor.TryAdd(_draft.Companions);
            SyncCompanionLegacyScalars();
            ApplyDraft();
            return WorkspaceEditResult.StayOpen("Companion app row added.");
        }
    }

    public WorkspaceEditResult RemoveCompanionRow(int index)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_draft.Companions.Count <= 1)
            {
                return WorkspaceEditResult.StayOpen();
            }

            PushEditSnapshot();
            CompanionAppFormEditor.TryRemove(_draft.Companions, index);
            SyncCompanionLegacyScalars();
            ApplyDraft();
            return WorkspaceEditResult.StayOpen("Companion app row removed.");
        }
    }

    public WorkspaceEditResult ApplyCompanionPreset(int index, string preset)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (index < 0 || index >= _draft.Companions.Count)
            {
                index = 0;
            }

            PushEditSnapshot();
            ApplyCompanionFormState(index, CompanionAppCatalog.CreateStateFromPreset(preset));
            PersistEditDraftIfNeeded();
            OnChanged();
            return WorkspaceEditResult.StayOpen();
        }
    }

    public WorkspaceEditResult SetCompanionExecutable(int index, string path)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (index < 0 || index >= _draft.Companions.Count)
            {
                index = 0;
            }

            var row = _draft.Companions[index];
            var preset = CompanionAppCatalog.ResolvePresetAfterBrowse(path);
            var args = CompanionAppArgumentValidation.NormalizeForSave(preset, path, row.Arguments);
            PushEditSnapshot();
            ApplyCompanionFormState(index, CompanionAppCatalog.ReconcileForForm(preset, path, args));
            PersistEditDraftIfNeeded();
            OnChanged();
            return WorkspaceEditResult.StayOpen();
        }
    }

    public WorkspaceEditResult Save()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _saveError = string.Empty;
            var draft = _draft;
            var originalName = string.IsNullOrWhiteSpace(draft.OriginalName) ? _originalName : draft.OriginalName;

            if (string.IsNullOrWhiteSpace(draft.Name) && !string.IsNullOrWhiteSpace(draft.Directory))
            {
                draft.Name = DeriveNameFromDirectory(draft.Directory);
                _autoFilledName = draft.Name;
            }

            if (string.IsNullOrWhiteSpace(draft.Directory))
            {
                PersistEditDraftIfNeeded();
                _saveError = "Folder path is required.";
                OnChanged();
                return WorkspaceEditResult.StayOpen(_saveError);
            }

            if (!ShortcutValidation.DirectoryExists(draft.Directory.Trim()))
            {
                PersistEditDraftIfNeeded();
                _saveError = $"Folder not found: {draft.Directory.Trim()}";
                OnChanged();
                return WorkspaceEditResult.StayOpen(_saveError);
            }

            if (string.IsNullOrWhiteSpace(draft.Name))
            {
                PersistEditDraftIfNeeded();
                _saveError = "Name is required.";
                OnChanged();
                return WorkspaceEditResult.StayOpen(_saveError);
            }

            CompanionAppFormEditor.EnsureAtLeastOne(draft.Companions);
            for (var i = 0; i < draft.Companions.Count; i++)
            {
                var row = draft.Companions[i];
                if (!CompanionAppCatalog.TryValidateFormSelection(row.Preset, row.Path, out var companionSelectionError))
                {
                    PersistEditDraftIfNeeded();
                    _saveError = companionSelectionError;
                    OnChanged();
                    return WorkspaceEditResult.StayOpen(_saveError);
                }

                if (!CompanionAppArgumentValidation.TryValidateForSave(row.Preset, row.Path, row.Arguments, out var companionArgumentError))
                {
                    PersistEditDraftIfNeeded();
                    _saveError = companionArgumentError;
                    OnChanged();
                    return WorkspaceEditResult.StayOpen(_saveError);
                }

                row.Arguments = CompanionAppArgumentValidation.NormalizeForSave(row.Preset, row.Path, row.Arguments);
                ApplyCompanionFormState(i, CompanionAppCatalog.ReconcileForSave(row.Preset, row.Path, row.Arguments, row.OpenOnLaunch), persist: false);
            }

            SyncCompanionLegacyScalars();

            var result = ShortcutFormSave.TrySave(
                originalName,
                draft.Name,
                draft.Abbreviation,
                draft.Directory,
                ShortcutFormLaunchSection.ToLaunchInputs(draft.Commands, draft.Name, draft.LaunchTarget),
                _services.Shortcuts,
                onSaved: null,
                draft.DevServerUrl,
                draft.RepoUrl,
                draft.OpenDevServerOnLaunch,
                draft.OpenCompanionAppOnLaunch,
                draft.CompanionAppPath,
                draft.CompanionAppArguments,
                CompanionAppFormEditor.ToCompanionEntries(draft.Companions));

            if (!result.Success)
            {
                PersistEditDraftIfNeeded();
                _saveError = result.Message;
                OnChanged();
                return WorkspaceEditResult.StayOpen(_saveError);
            }

            _saveError = string.Empty;
            _services.Drafts.Clear();
            _baselineDraft = CloneDraft(_draft);
            try
            {
                _onSaved?.Invoke();
            }
            catch (Exception)
            {
                // Best-effort; repository write already succeeded.
            }

            return WorkspaceEditResult.Saved(
                string.IsNullOrWhiteSpace(result.Message) ? "Workspace saved." : result.Message);
        }
    }

    public WorkspaceEditResult Cancel()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _saveError = string.Empty;
            if (!HasUnsavedChanges)
            {
                _services.Drafts.Clear();
                LeaveForm();
                return WorkspaceEditResult.Cancelled();
            }

            PersistEditDraftIfNeeded();
            return WorkspaceEditResult.PromptDiscard();
        }
    }

    public WorkspaceEditResult Discard()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _services.Drafts.Clear();
            _draft = CloneDraft(_baselineDraft);
            _saveError = string.Empty;
            OnChanged();
            LeaveForm();
            return WorkspaceEditResult.Discarded();
        }
    }

    public bool TryUndo()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_editHistory.TryUndo(CaptureEditSnapshot(), out var restored))
            {
                return false;
            }

            return ApplyEditSnapshot(restored);
        }
    }

    public bool TryRedo()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_editHistory.TryRedo(CaptureEditSnapshot(), out var restored))
            {
                return false;
            }

            return ApplyEditSnapshot(restored);
        }
    }

    public void LeaveForm()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            UnsubscribeFromDraftCleared();
            CancelScan();
            PersistEditDraftIfNeeded();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            UnsubscribeFromDraftCleared();
            CancelScan();
            PersistEditDraftIfNeeded();
            _disposed = true;
            _editHistory.Clear();
        }
    }

    private void InitializeDraft(TerminalShortcut? existing, TerminalShortcut? createSeed)
    {
        _originalName = existing?.Name;

        var initial = existing ?? createSeed;
        var launchTarget = TerminalCatalog.EncodeLaunchTargetId(initial ?? new TerminalShortcut());
        var commands = ShortcutFormLaunchSection.CommandsFromShortcut(initial, launchTarget);
        var companions = CompanionAppFormEditor.FromShortcut(initial);
        CompanionAppFormEditor.SyncLegacyScalars(companions, out var openCompanion, out var companionPath, out var companionArgs, out var companionPreset);

        _draft = new FormDraft
        {
            OriginalName = existing?.Name ?? string.Empty,
            Name = initial?.Name ?? string.Empty,
            Abbreviation = initial?.Abbreviation ?? string.Empty,
            Directory = initial?.Directory ?? string.Empty,
            DevServerUrl = initial?.DevServerUrl ?? string.Empty,
            RepoUrl = initial?.RepoUrl ?? string.Empty,
            OpenDevServerOnLaunch = initial?.OpenDevServerOnLaunch ?? false,
            Companions = companions,
            OpenCompanionAppOnLaunch = openCompanion,
            CompanionAppPreset = companionPreset,
            CompanionAppPath = companionPath,
            CompanionAppArguments = companionArgs,
            Commands = commands,
            LaunchTarget = launchTarget,
            RunAsAdmin = commands.Count > 0 && commands[0].RunAsAdmin,
        };

        OnChanged();

        if (_originalName is not null && !_subscribedToDraftCleared)
        {
            var weakSelf = new WeakReference<WorkspaceEditor>(this);
            Action<string>? handler = null;
            handler = originalName =>
            {
                if (weakSelf.TryGetTarget(out var self))
                {
                    self.OnDraftStoreCleared(originalName);
                }
                else if (handler is not null)
                {
                    _services.Drafts.Cleared -= handler;
                }
            };

            _draftClearedHandler = handler;
            _services.Drafts.Cleared += handler;
            _subscribedToDraftCleared = true;
        }
    }

    private void OnDraftStoreCleared(string originalName)
    {
        lock (_sync)
        {
            if (_originalName is null
                || !string.Equals(originalName, _originalName, StringComparison.OrdinalIgnoreCase)
                || _disposed)
            {
                return;
            }

            ResetToSavedBaseline();
        }
    }

    private void ResetToSavedBaseline()
    {
        var saved = _services.Shortcuts.GetByName(_originalName!);
        if (saved is null)
        {
            return;
        }

        _saveError = string.Empty;
        _nameCustomized = false;
        _autoFilledName = null;

        var launchTarget = TerminalCatalog.EncodeLaunchTargetId(saved);
        var commands = ShortcutFormLaunchSection.CommandsFromShortcut(saved, launchTarget);
        var companions = CompanionAppFormEditor.FromShortcut(saved);
        CompanionAppFormEditor.SyncLegacyScalars(companions, out var openCompanion, out var companionPath, out var companionArgs, out var companionPreset);

        _draft = new FormDraft
        {
            OriginalName = saved.Name,
            Name = saved.Name,
            Abbreviation = saved.Abbreviation ?? string.Empty,
            Directory = saved.Directory,
            DevServerUrl = saved.DevServerUrl ?? string.Empty,
            RepoUrl = saved.RepoUrl ?? string.Empty,
            Companions = companions,
            OpenCompanionAppOnLaunch = openCompanion,
            CompanionAppPreset = companionPreset,
            CompanionAppPath = companionPath,
            CompanionAppArguments = companionArgs,
            Commands = commands,
            LaunchTarget = launchTarget,
            RunAsAdmin = commands.Count > 0 && commands[0].RunAsAdmin,
        };

        _baselineDraft = CloneDraft(_draft);
        OnChanged();
    }

    private void UnsubscribeFromDraftCleared()
    {
        if (!_subscribedToDraftCleared || _draftClearedHandler is null)
        {
            return;
        }

        _services.Drafts.Cleared -= _draftClearedHandler;
        _subscribedToDraftCleared = false;
    }

    private void ApplyDirectorySelection(string directory)
    {
        if (!ShortcutValidation.TryNormalizeDirectory(directory, out var normalized, out _))
        {
            normalized = directory.Trim();
        }

        _draft.Directory = normalized;

        if (ShouldAutofillNameFromDirectory())
        {
            _draft.Name = DeriveNameFromDirectory(normalized);
            _autoFilledName = _draft.Name;
        }

        if (string.IsNullOrWhiteSpace(_draft.RepoUrl))
        {
            _draft.RepoUrl = GitRepoDiscovery.TryGetRemoteUrl(normalized) ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(_draft.DevServerUrl))
        {
            _draft.DevServerUrl = _services.ProjectAnalysis.TryDetectDevServerUrl(normalized) ?? string.Empty;
        }

        InvalidateSuggestionScan();
    }

    private bool ShouldAutofillNameFromDirectory()
    {
        if (string.IsNullOrWhiteSpace(_draft.Name))
        {
            _nameCustomized = false;
            return true;
        }

        if (_nameCustomized)
        {
            return false;
        }

        if (_autoFilledName is null)
        {
            return false;
        }

        return string.Equals(
            Normalize(_draft.Name),
            Normalize(_autoFilledName),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string DeriveNameFromDirectory(string directory)
    {
        var trimmed = directory.Trim().TrimEnd('\\', '/');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        var leaf = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(leaf) ? trimmed : leaf;
    }

    private void TryRestoreEditDraft()
    {
        if (_originalName is null)
        {
            return;
        }

        if (!_services.Drafts.TryGetForRestore(_originalName, out var persisted))
        {
            return;
        }

        var restored = ShortcutFormDraftData.FromPersisted(persisted);
        _showRestoredDraftNote = true;
        List<LaunchRowDraft> commands = restored.Launches.Count > 0
            ? [.. restored.Launches.Select(launch => new LaunchRowDraft
            {
                Id = string.IsNullOrWhiteSpace(launch.Id) ? Guid.NewGuid().ToString("N") : launch.Id,
                Command = launch.Command,
                TaskType = TaskTypeCatalog.Normalize(launch.TaskType),
                LaunchTarget = string.IsNullOrWhiteSpace(launch.LaunchTarget)
                    ? restored.LaunchTarget
                    : launch.LaunchTarget,
                RunAsAdmin = launch.RunAsAdmin,
            })]
            : ShortcutFormLaunchSection.CommandsFromShortcut(null, restored.LaunchTarget);

        LaunchRowListEditor.EnsureMinimumRowsForEditor(commands, restored.LaunchTarget);

        if (commands.Count > 0 && restored.Launches.Count == 0 && !string.IsNullOrWhiteSpace(restored.Command))
        {
            commands[0].Command = restored.Command;
            commands[0].RunAsAdmin = restored.RunAsAdmin;
        }

        List<CompanionAppFormRow> companions = restored.Companions.Count > 0
            ? [.. restored.Companions.Select(c => new CompanionAppFormRow
            {
                Id = string.IsNullOrWhiteSpace(c.Id) ? Guid.NewGuid().ToString("N") : c.Id,
                Preset = c.Preset,
                Path = c.Path,
                Arguments = c.Arguments,
                OpenOnLaunch = c.OpenOnLaunch,
            })]
            : CompanionAppFormEditor.FromShortcut(new TerminalShortcut
            {
                OpenCompanionAppOnLaunch = restored.OpenCompanionAppOnLaunch,
                CompanionAppPath = restored.CompanionAppPath,
                CompanionAppArguments = restored.CompanionAppArguments,
            });

        CompanionAppFormEditor.SyncLegacyScalars(companions, out var openCompanion, out var companionPath, out var companionArgs, out var companionPreset);

        _draft = new FormDraft
        {
            OriginalName = restored.OriginalName,
            Name = restored.Name,
            Abbreviation = restored.Abbreviation,
            Directory = restored.Directory,
            DevServerUrl = restored.DevServerUrl,
            RepoUrl = restored.RepoUrl,
            OpenDevServerOnLaunch = restored.OpenDevServerOnLaunch,
            Companions = companions,
            OpenCompanionAppOnLaunch = openCompanion,
            CompanionAppPreset = companionPreset,
            CompanionAppPath = companionPath,
            CompanionAppArguments = companionArgs,
            Commands = commands,
            LaunchTarget = restored.LaunchTarget,
            RunAsAdmin = commands.Count > 0 ? commands[0].RunAsAdmin : restored.RunAsAdmin,
        };

        _nameCustomized = persisted.NameCustomized;
        _autoFilledName = persisted.AutoFilledName;
        _baselineDraft = CloneDraft(_draft);
        OnChanged();
    }

    private void ScheduleSuggestionScan()
    {
        if (_suggestionScanComplete)
        {
            return;
        }

        var directory = _draft.Directory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            _suggestionScanComplete = true;
            return;
        }

        var generation = Interlocked.Increment(ref _scanGeneration);
        var usedCommands = _draft.Commands.Select(command => command.Command).ToArray();
        var token = _scanCts?.Token ?? CancellationToken.None;

        _ = Task.Run(() =>
        {
            try
            {
                if (!token.IsCancellationRequested)
                {
                    _ = CommandSuggestionService.GetPills(directory, usedCommands, _services.ProjectAnalysis, _services.ClassificationCache);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when scan is canceled.
            }
            catch (IOException)
            {
                // Best effort — form remains usable without pills.
            }
            catch (UnauthorizedAccessException)
            {
                // Best effort — form remains usable without pills.
            }
            catch (ArgumentException)
            {
                // Best effort — form remains usable without pills.
            }
            catch (InvalidOperationException)
            {
                // Best effort — form remains usable without pills.
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            lock (_sync)
            {
                if (_disposed || generation != _scanGeneration)
                {
                    return;
                }

                _suggestionScanComplete = true;
                OnChanged();
            }
        }, token);
    }

    private void CancelScan()
    {
        Interlocked.Increment(ref _scanGeneration);
        var cts = _scanCts;
        _scanCts = null;
        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Best effort.
        }

        try
        {
            cts?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Best effort.
        }
    }

    private void InvalidateSuggestionScan()
    {
        _suggestionScanComplete = false;
        Interlocked.Increment(ref _scanGeneration);
        ScheduleSuggestionScan();
    }

    private void ApplyDraft(bool persist = true)
    {
        CompanionAppFormEditor.EnsureAtLeastOne(_draft.Companions);

        if (persist && _baselineReady)
        {
            PersistEditDraftIfNeeded();
        }

        OnChanged();
    }

    private void PersistEditDraftIfNeeded()
    {
        if (_originalName is null)
        {
            return;
        }

        _services.Drafts.SaveIfDirty(
            _originalName,
            ToDraftData(_draft),
            ToDraftData(_baselineDraft),
            _nameCustomized,
            _autoFilledName);
    }

    private static ShortcutFormDraftData ToDraftData(FormDraft draft)
    {
        var first = draft.Commands.FirstOrDefault();
        return new ShortcutFormDraftData
        {
            OriginalName = draft.OriginalName,
            Name = draft.Name,
            Abbreviation = draft.Abbreviation,
            Directory = draft.Directory,
            Command = first?.Command ?? string.Empty,
            LaunchTarget = draft.LaunchTarget,
            DevServerUrl = draft.DevServerUrl,
            RepoUrl = draft.RepoUrl,
            OpenDevServerOnLaunch = draft.OpenDevServerOnLaunch,
            OpenCompanionAppOnLaunch = draft.OpenCompanionAppOnLaunch,
            CompanionAppPreset = draft.CompanionAppPreset,
            CompanionAppPath = draft.CompanionAppPath,
            CompanionAppArguments = draft.CompanionAppArguments,
            Companions = [.. draft.Companions.Select(row => new ShortcutFormCompanionDraftData
            {
                Id = row.Id,
                Preset = row.Preset,
                Path = row.Path,
                Arguments = row.Arguments,
                OpenOnLaunch = row.OpenOnLaunch,
            })],
            RunAsAdmin = first?.RunAsAdmin ?? draft.RunAsAdmin,
            Launches = [.. draft.Commands.Select(command => new ShortcutFormLaunchDraftData
            {
                Id = command.Id,
                Command = command.Command,
                LaunchTarget = command.LaunchTarget,
                RunAsAdmin = command.RunAsAdmin,
                IsEnabled = true,
                TaskType = command.TaskType,
            })],
        };
    }

    private bool MergeDraftFromInputs(string payload, bool excludeDirectory = false)
    {
        var data = JsonNode.Parse(payload)?.AsObject();
        if (data is null)
        {
            return false;
        }

        if (data.Count == 0)
        {
            return true;
        }

        var mergedName = data["Name"]?.ToString() ?? _draft.Name;
        UpdateAutoFilledNameTracking(mergedName);

        var previousCompanions = _draft.Companions.Select(row => row.Clone()).ToList();
        var mergedCompanions = MergeCompanionsFromInputs(data, previousCompanions);

        _draft = new FormDraft
        {
            OriginalName = data["OriginalName"]?.ToString() ?? _draft.OriginalName,
            Name = mergedName,
            Abbreviation = data["Abbreviation"]?.ToString() ?? _draft.Abbreviation,
            Directory = excludeDirectory
                ? _draft.Directory
                : data["Directory"]?.ToString() ?? _draft.Directory,
            Commands = MergeCommandsFromInputs(data, _draft.Commands),
            LaunchTarget = data["LaunchTarget_0"]?.ToString()
                ?? data["LaunchTarget"]?.ToString()
                ?? _draft.LaunchTarget,
            DevServerUrl = data["DevServerUrl"]?.ToString() ?? _draft.DevServerUrl,
            RepoUrl = data["RepoUrl"]?.ToString() ?? _draft.RepoUrl,
            OpenDevServerOnLaunch = ParseToggleBool(
                data["OpenDevServerOnLaunch"]?.ToString(),
                _draft.OpenDevServerOnLaunch),
            Companions = mergedCompanions,
            OpenCompanionAppOnLaunch = _draft.OpenCompanionAppOnLaunch,
            CompanionAppPreset = _draft.CompanionAppPreset,
            CompanionAppPath = _draft.CompanionAppPath,
            CompanionAppArguments = _draft.CompanionAppArguments,
            RunAsAdmin = false,
        };

        SyncDraftRunAsAdminFromCommands();
        _ = ApplyCompanionPresetChanges(previousCompanions, mergedCompanions);
        SyncCompanionLegacyScalars();

        return true;
    }

    private bool ApplyCompanionPresetChanges(
        List<CompanionAppFormRow> previous,
        List<CompanionAppFormRow> current)
    {
        var changed = false;
        var count = Math.Min(previous.Count, current.Count);
        for (var i = 0; i < count; i++)
        {
            if (string.Equals(previous[i].Preset, current[i].Preset, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ApplyCompanionFormState(i, CompanionAppCatalog.CreateStateFromPreset(current[i].Preset));
            changed = true;
        }

        return changed;
    }

    private void ApplyCompanionFormState(int index, CompanionAppCatalog.CompanionAppFormState state, bool persist = true)
    {
        CompanionAppFormEditor.EnsureAtLeastOne(_draft.Companions);
        if (index < 0 || index >= _draft.Companions.Count)
        {
            index = 0;
        }

        var row = _draft.Companions[index];
        row.Preset = state.Preset;
        row.Path = state.Path;
        row.Arguments = state.Arguments;
        row.OpenOnLaunch = state.LaunchOnWorkspaceOpen;
        SyncCompanionLegacyScalars();

        if (persist && _baselineReady)
        {
            PersistEditDraftIfNeeded();
            OnChanged();
        }
    }

    private void SyncCompanionLegacyScalars()
    {
        CompanionAppFormEditor.SyncLegacyScalars(
            _draft.Companions,
            out var openOnLaunch,
            out var path,
            out var arguments,
            out var preset);

        _draft.OpenCompanionAppOnLaunch = openOnLaunch;
        _draft.CompanionAppPath = path;
        _draft.CompanionAppArguments = arguments;
        _draft.CompanionAppPreset = preset;
    }

    private static List<CompanionAppFormRow> MergeCompanionsFromInputs(
        JsonObject data,
        List<CompanionAppFormRow> existing)
    {
        var formCount = 0;
        for (var probe = 0; probe < CompanionAppFormEditor.MaxCount; probe++)
        {
            if (!data.ContainsKey($"CompanionAppPreset_{probe}"))
            {
                break;
            }

            formCount = probe + 1;
        }

        var count = formCount > 0 ? formCount : Math.Max(1, existing.Count);
        List<CompanionAppFormRow> merged = [];
        for (var i = 0; i < count; i++)
        {
            var prior = i < existing.Count ? existing[i] : CompanionAppFormRow.Empty();
            merged.Add(new CompanionAppFormRow
            {
                Id = prior.Id,
                Preset = data[$"CompanionAppPreset_{i}"]?.ToString() ?? prior.Preset,
                Path = prior.Path,
                Arguments = data[$"CompanionAppArguments_{i}"]?.ToString() ?? prior.Arguments,
                OpenOnLaunch = prior.OpenOnLaunch,
            });
        }

        CompanionAppFormEditor.EnsureAtLeastOne(merged);
        return merged;
    }

    private static List<LaunchRowDraft> MergeCommandsFromInputs(
        JsonObject data,
        List<LaunchRowDraft> existing)
    {
        var count = existing.Count;
        for (var probe = 0; probe < 64; probe++)
        {
            if (!data.ContainsKey($"LaunchCommand_{probe}"))
            {
                count = probe;
                break;
            }
        }

        if (count == 0)
        {
            return [.. existing];
        }

        List<LaunchRowDraft> merged = [];
        for (var i = 0; i < count; i++)
        {
            var prior = i < existing.Count ? existing[i] : new();
            merged.Add(new LaunchRowDraft
            {
                Id = prior.Id,
                Command = data[$"LaunchCommand_{i}"]?.ToString() ?? prior.Command,
                TaskType = TaskTypeCatalog.Normalize(data[$"LaunchType_{i}"]?.ToString() ?? prior.TaskType),
                LaunchTarget = data[$"LaunchTarget_{i}"]?.ToString()
                    ?? prior.LaunchTarget,
                RunAsAdmin = ParseToggleBool(data[$"LaunchRunAsAdmin_{i}"]?.ToString(), prior.RunAsAdmin),
                IsEditorPlaceholder = prior.IsEditorPlaceholder,
            });
        }

        return merged;
    }

    private string GetDefaultRowLaunchTarget()
    {
        if (_draft.Commands.Count > 0 && !string.IsNullOrWhiteSpace(_draft.Commands[0].LaunchTarget))
        {
            return _draft.Commands[0].LaunchTarget;
        }

        return string.IsNullOrWhiteSpace(_draft.LaunchTarget) ? "default" : _draft.LaunchTarget;
    }

    private void SyncDraftLaunchTargetFromCommands()
    {
        if (_draft.Commands.Count > 0)
        {
            _draft.LaunchTarget = _draft.Commands[0].LaunchTarget;
        }
    }

    private void SyncDraftRunAsAdminFromCommands()
    {
        _draft.RunAsAdmin = _draft.Commands.Count > 0 && _draft.Commands[0].RunAsAdmin;
    }

    private void UpdateAutoFilledNameTracking(string mergedName)
    {
        if (string.IsNullOrWhiteSpace(mergedName))
        {
            _nameCustomized = false;
            _autoFilledName = null;
            return;
        }

        if (_autoFilledName is not null
            && !string.Equals(
                Normalize(mergedName),
                Normalize(_autoFilledName),
                StringComparison.OrdinalIgnoreCase))
        {
            _autoFilledName = null;
            _nameCustomized = true;
            return;
        }

        if (_autoFilledName is null
            && !string.IsNullOrWhiteSpace(mergedName)
            && !string.IsNullOrWhiteSpace(_draft.Directory))
        {
            var derived = DeriveNameFromDirectory(_draft.Directory);
            if (!string.Equals(
                    Normalize(mergedName),
                    Normalize(derived),
                    StringComparison.OrdinalIgnoreCase))
            {
                _nameCustomized = true;
            }
        }
    }

    private void PushEditSnapshot() => _editHistory.PushBeforeChange(CaptureEditSnapshot());

    private FormEditSnapshot CaptureEditSnapshot() =>
        new()
        {
            Commands = LaunchRowListEditor.CloneRows(_draft.Commands),
            Companions = [.. _draft.Companions.Select(row => row.Clone())],
            ExpandSuggestionPills = _draft.ExpandSuggestionPills,
            AutoFilledName = _autoFilledName,
            NameCustomized = _nameCustomized,
        };

    private bool ApplyEditSnapshot(FormEditSnapshot restored)
    {
        _draft.Commands = restored.Commands;
        _draft.Companions = [.. restored.Companions.Select(row => row.Clone())];
        CompanionAppFormEditor.EnsureAtLeastOne(_draft.Companions);
        SyncCompanionLegacyScalars();
        _draft.ExpandSuggestionPills = restored.ExpandSuggestionPills;
        _autoFilledName = restored.AutoFilledName;
        _nameCustomized = restored.NameCustomized;
        SyncDraftLaunchTargetFromCommands();
        SyncDraftRunAsAdminFromCommands();
        ApplyDraft();
        return true;
    }

    private WorkspaceEditState BuildState()
    {
        var scanning = IsSuggestionScanning;
        IReadOnlyList<CommandSuggestionPill> pills = scanning
            ? []
            : CommandSuggestionService.GetPills(
                _draft.Directory,
                _draft.Commands.Select(c => c.Command),
                _services.ProjectAnalysis,
                _services.ClassificationCache);

        return new WorkspaceEditState(
            _originalName,
            _draft.Name,
            _draft.Abbreviation,
            _draft.Directory,
            _draft.LaunchTarget,
            _draft.DevServerUrl,
            _draft.RepoUrl,
            _draft.OpenDevServerOnLaunch,
            _draft.OpenCompanionAppOnLaunch,
            _draft.CompanionAppPreset,
            _draft.CompanionAppPath,
            _draft.CompanionAppArguments,
            [.. _draft.Commands.Select(c => c.Clone())],
            [.. _draft.Companions.Select(c => c.Clone())],
            pills,
            _draft.ExpandSuggestionPills,
            scanning,
            _showRestoredDraftNote,
            string.IsNullOrWhiteSpace(_saveError) ? null : _saveError);
    }

    private void OnChanged()
    {
        WorkspaceEditState state;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            state = BuildState();
        }

        Changed?.Invoke(this, new WorkspaceEditChangedEventArgs(state));
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim();

    private static bool ParseToggleBool(string? value, bool fallback) =>
        value switch
        {
            "true" => true,
            "false" => false,
            _ => fallback,
        };

    private static FormDraft CloneDraft(FormDraft draft) =>
        new()
        {
            OriginalName = draft.OriginalName,
            Name = draft.Name,
            Abbreviation = draft.Abbreviation,
            Directory = draft.Directory,
            Commands = [.. draft.Commands.Select(command => new LaunchRowDraft
            {
                Id = command.Id,
                Command = command.Command,
                TaskType = command.TaskType,
                LaunchTarget = command.LaunchTarget,
                RunAsAdmin = command.RunAsAdmin,
                IsEditorPlaceholder = command.IsEditorPlaceholder,
            })],
            LaunchTarget = draft.LaunchTarget,
            DevServerUrl = draft.DevServerUrl,
            RepoUrl = draft.RepoUrl,
            OpenDevServerOnLaunch = draft.OpenDevServerOnLaunch,
            Companions = [.. draft.Companions.Select(row => row.Clone())],
            OpenCompanionAppOnLaunch = draft.OpenCompanionAppOnLaunch,
            CompanionAppPreset = draft.CompanionAppPreset,
            CompanionAppPath = draft.CompanionAppPath,
            CompanionAppArguments = draft.CompanionAppArguments,
            RunAsAdmin = draft.RunAsAdmin,
            ExpandSuggestionPills = draft.ExpandSuggestionPills,
        };

    private static bool DraftEquals(FormDraft left, FormDraft right)
    {
        if (!string.Equals(Normalize(left.Name), Normalize(right.Name), StringComparison.Ordinal)
            || !string.Equals(Normalize(left.Abbreviation), Normalize(right.Abbreviation), StringComparison.Ordinal)
            || !string.Equals(Normalize(left.Directory), Normalize(right.Directory), StringComparison.Ordinal)
            || !string.Equals(Normalize(left.LaunchTarget), Normalize(right.LaunchTarget), StringComparison.Ordinal)
            || !string.Equals(Normalize(left.DevServerUrl), Normalize(right.DevServerUrl), StringComparison.Ordinal)
            || !string.Equals(Normalize(left.RepoUrl), Normalize(right.RepoUrl), StringComparison.Ordinal)
            || left.OpenDevServerOnLaunch != right.OpenDevServerOnLaunch
            || left.OpenCompanionAppOnLaunch != right.OpenCompanionAppOnLaunch
            || !string.Equals(Normalize(left.CompanionAppPreset), Normalize(right.CompanionAppPreset), StringComparison.Ordinal)
            || !string.Equals(Normalize(left.CompanionAppPath), Normalize(right.CompanionAppPath), StringComparison.Ordinal)
            || !string.Equals(Normalize(left.CompanionAppArguments), Normalize(right.CompanionAppArguments), StringComparison.Ordinal)
            || left.RunAsAdmin != right.RunAsAdmin
            || left.Companions.Count != right.Companions.Count)
        {
            return false;
        }

        if (left.Commands.Count != right.Commands.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Commands.Count; i++)
        {
            if (!string.Equals(Normalize(left.Commands[i].Command), Normalize(right.Commands[i].Command), StringComparison.Ordinal)
                || !string.Equals(TaskTypeCatalog.Normalize(left.Commands[i].TaskType), TaskTypeCatalog.Normalize(right.Commands[i].TaskType), StringComparison.Ordinal)
                || !string.Equals(Normalize(left.Commands[i].LaunchTarget), Normalize(right.Commands[i].LaunchTarget), StringComparison.Ordinal)
                || left.Commands[i].RunAsAdmin != right.Commands[i].RunAsAdmin)
            {
                return false;
            }
        }

        for (var i = 0; i < left.Companions.Count; i++)
        {
            if (!string.Equals(Normalize(left.Companions[i].Preset), Normalize(right.Companions[i].Preset), StringComparison.Ordinal)
                || !string.Equals(Normalize(left.Companions[i].Path), Normalize(right.Companions[i].Path), StringComparison.Ordinal)
                || !string.Equals(Normalize(left.Companions[i].Arguments), Normalize(right.Companions[i].Arguments), StringComparison.Ordinal)
                || left.Companions[i].OpenOnLaunch != right.Companions[i].OpenOnLaunch)
            {
                return false;
            }
        }

        return true;
    }

    private sealed class FormDraft
    {
        public string OriginalName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Abbreviation { get; set; } = string.Empty;
        public string Directory { get; set; } = string.Empty;
        public string DevServerUrl { get; set; } = string.Empty;
        public bool OpenDevServerOnLaunch { get; set; }
        public string RepoUrl { get; set; } = string.Empty;
        public List<CompanionAppFormRow> Companions { get; set; } = [CompanionAppFormRow.Empty()];
        public bool OpenCompanionAppOnLaunch { get; set; }
        public string CompanionAppPreset { get; set; } = CompanionAppCatalog.PresetNone;
        public string CompanionAppPath { get; set; } = string.Empty;
        public string CompanionAppArguments { get; set; } = string.Empty;
        public List<LaunchRowDraft> Commands { get; set; } = [new LaunchRowDraft()];
        public string LaunchTarget { get; set; } = "default";
        public bool RunAsAdmin { get; set; }
        public bool ExpandSuggestionPills { get; set; }
    }

    private sealed class FormEditSnapshot
    {
        public List<LaunchRowDraft> Commands { get; set; } = [];
        public List<CompanionAppFormRow> Companions { get; set; } = [];
        public bool ExpandSuggestionPills { get; set; }
        public string? AutoFilledName { get; set; }
        public bool NameCustomized { get; set; }

        public FormEditSnapshot Clone() =>
            new()
            {
                Commands = LaunchRowListEditor.CloneRows(Commands),
                Companions = Companions.Select(row => row.Clone()).ToList(),
                ExpandSuggestionPills = ExpandSuggestionPills,
                AutoFilledName = AutoFilledName,
                NameCustomized = NameCustomized,
            };
    }
}
