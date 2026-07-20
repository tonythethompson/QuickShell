using System.Linq;
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
            // Must match BuildDataFields / BuildSelectablePills so Open directory only (blank command)
            // and pillIndex slots resolve the same list the Adaptive Card rendered.
            var pills = SuggestionPillPresentation.BuildSelectablePills(
                _draft.Directory,
                _draft.Commands.Select(c => c.Command),
                _services.ProjectAnalysis,
                _services.CommandSuggestions);

            var pill = _services.CommandSuggestions.TryFindPill(pills, command, taskType);
            if (pill is null && pillIndex >= 0 && pillIndex < pills.Count)
            {
                pill = pills[pillIndex];
            }

            if (pill is null)
            {
                return WorkspaceEditResult.StayOpen("That suggestion is no longer available.");
            }

            PushEditSnapshot();
            _ = _services.CommandSuggestions.ApplyPill(_draft.Commands, pill, GetDefaultRowLaunchTarget());
            ApplyDraft();
            var toast = ReferenceEquals(pill, SuggestionPillPresentation.OpenToDirectoryPill)
                ? "Added Open directory only."
                : $"Added {pill.TypeTitle} command.";
            return WorkspaceEditResult.StayOpen(toast);
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
            foreach (var command in _draft.Commands.Where(command => !availableTargetIds.Any(t => t.Equals(command.LaunchTarget, StringComparison.OrdinalIgnoreCase))))
            {
                command.LaunchTarget = defaultTargetId;
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
            catch (Exception) when (!System.Diagnostics.Debugger.IsAttached)
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

    private WorkspaceEditState BuildState()
    {
        var scanning = IsSuggestionScanning;
        IReadOnlyList<CommandSuggestionPill> pills = scanning
            ? []
            : _services.CommandSuggestions.GetPills(
                _draft.Directory,
                _draft.Commands.Select(c => c.Command),
                _services.ProjectAnalysis);

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
