using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using QuickShell.Abstractions;
using QuickShell.Models;
using QuickShell.Pages;

namespace QuickShell.Services.WorkspaceEditor;

internal sealed partial class WorkspaceEditor
{
    /// <summary>
    /// Initializes the editable draft from an existing shortcut or creation seed.
    /// </summary>
    /// <param name="existing">The shortcut being edited, when available.</param>
    /// <param name="createSeed">The initial shortcut values to use when no existing shortcut is provided.</param>
    private void InitializeDraft(TerminalShortcut? existing, TerminalShortcut? createSeed)
    {
        _originalName = existing?.Name;

        var initial = existing ?? createSeed;
        _draft = BuildDraftFromShortcut(initial, existing?.Name ?? string.Empty);

        OnChanged();

        if (_originalName is not null && !_subscribedToDraftCleared)
        {
            // Capture the drafts service in a local rather than referencing
            // _services inside the handler: touching an instance field would
            // implicitly capture `this` in the closure, keeping this editor
            // alive for as long as it stays subscribed and defeating the
            // WeakReference below.
            var drafts = _services.Drafts;
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
                    drafts.Cleared -= handler;
                }
            };

            _draftClearedHandler = handler;
            drafts.Cleared += handler;
            _subscribedToDraftCleared = true;
        }
    }

    /// <summary>
    /// Resets the editor to the saved baseline when its associated draft is cleared.
    /// </summary>
    /// <param name="originalName">The name identifying the cleared draft.</param>
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

    /// <summary>
    /// Resets the editor draft to the currently saved shortcut and clears draft-specific state.
    /// </summary>
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

        _draft = BuildDraftFromShortcut(saved, saved.Name);

        _baselineDraft = CloneDraft(_draft);
        OnChanged();
    }

    /// <summary>
    /// Builds a form draft from a shortcut's saved values, or from defaults when no shortcut is given.
    /// </summary>
    /// <param name="source">The shortcut whose values seed the draft, when available.</param>
    /// <param name="originalName">The original name to record on the draft.</param>
    /// <returns>A new form draft populated from <paramref name="source"/>.</returns>
    private static FormDraft BuildDraftFromShortcut(TerminalShortcut? source, string originalName)
    {
        var launchTarget = TerminalCatalog.EncodeLaunchTargetId(source ?? new TerminalShortcut());
        var commands = ShortcutFormLaunchSection.CommandsFromShortcut(source, launchTarget);
        var companions = CompanionAppFormEditor.FromShortcut(source);
        CompanionAppFormEditor.SyncLegacyScalars(companions, out var openCompanion, out var companionPath, out var companionArgs, out var companionPreset);

        return new FormDraft
        {
            OriginalName = originalName,
            Name = source?.Name ?? string.Empty,
            Abbreviation = source?.Abbreviation ?? string.Empty,
            Directory = source?.Directory ?? string.Empty,
            DevServerUrl = source?.DevServerUrl ?? string.Empty,
            RepoUrl = source?.RepoUrl ?? string.Empty,
            OpenDevServerOnLaunch = source?.OpenDevServerOnLaunch ?? false,
            Companions = companions,
            OpenCompanionAppOnLaunch = openCompanion,
            CompanionAppPreset = companionPreset,
            CompanionAppPath = companionPath,
            CompanionAppArguments = companionArgs,
            Commands = commands,
            LaunchTarget = launchTarget,
            RunAsAdmin = commands.Count > 0 && commands[0].RunAsAdmin,
        };
    }

    /// <summary>
    /// Removes the draft-cleared event handler when it is subscribed.
    /// </summary>
    private void UnsubscribeFromDraftCleared()
    {
        if (!_subscribedToDraftCleared || _draftClearedHandler is null)
        {
            return;
        }

        _services.Drafts.Cleared -= _draftClearedHandler;
        _subscribedToDraftCleared = false;
    }

    /// <summary>
    /// Restores the persisted edit draft for the current shortcut, including its launch and companion settings.
    /// </summary>
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

        // TerminalShortcut has no CompanionAppPreset scalar, so FromShortcut above
        // can only infer a preset from the path. Prefer the persisted preset choice
        // when one was recorded, rather than silently re-inferring it.
        if (restored.Companions.Count == 0
            && !string.IsNullOrWhiteSpace(restored.CompanionAppPreset)
            && companions.Count > 0)
        {
            companions[0].Preset = restored.CompanionAppPreset;
        }

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
        // Keep the saved-shortcut baseline so restored edits still count as unsaved.
        OnChanged();
    }

    /// <summary>
    /// Applies the current draft and optionally persists it when a saved baseline is available.
    /// </summary>
    /// <param name="persist">Whether to persist the draft when changes are ready to be saved.</param>
    private void ApplyDraft(bool persist = true)
    {
        CompanionAppFormEditor.EnsureAtLeastOne(_draft.Companions);

        if (persist && _baselineReady)
        {
            PersistEditDraftIfNeeded();
        }

        OnChanged();
    }

    /// <summary>
    /// Persists the current edit draft when it differs from the saved baseline.
    /// </summary>
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

    /// <summary>
    /// Converts the form draft into its persisted draft-data representation.
    /// </summary>
    /// <param name="draft">The draft to convert.</param>
    /// <returns>The persisted representation of the draft, including companion and launch entries.</returns>
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

    /// <summary>
    /// Merges JSON editor input into the current workspace draft.
    /// </summary>
    /// <param name="payload">The JSON payload containing draft field values.</param>
    /// <param name="excludeDirectory">Whether to preserve the current directory value.</param>
    /// <returns><c>true</c> if the payload is valid, including when it contains no fields; <c>false</c> otherwise.</returns>
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
        var mergedDirectory = excludeDirectory
            ? _draft.Directory
            : data["Directory"]?.ToString() ?? _draft.Directory;
        UpdateAutoFilledNameTracking(mergedName, mergedDirectory);

        var previousCompanions = _draft.Companions.Select(row => row.Clone()).ToList();
        var mergedCompanions = MergeCompanionsFromInputs(data, previousCompanions);

        _draft = new FormDraft
        {
            OriginalName = data["OriginalName"]?.ToString() ?? _draft.OriginalName,
            Name = mergedName,
            Abbreviation = data["Abbreviation"]?.ToString() ?? _draft.Abbreviation,
            Directory = mergedDirectory,
            Commands = MergeCommandsFromInputs(data, _draft.Commands),
            LaunchTarget = data["LaunchTarget_0"]?.ToString()
                ?? data["LaunchTarget"]?.ToString()
                ?? _draft.LaunchTarget,
            DevServerUrl = data["DevServerUrl"]?.ToString() ?? _draft.DevServerUrl,
            RepoUrl = data["RepoUrl"]?.ToString() ?? _draft.RepoUrl,
            OpenDevServerOnLaunch = ParseToggleBool(
                data["OpenDevServerOnLaunch"]?.ToString(),
                _draft.OpenDevServerOnLaunch),
            ExpandSuggestionPills = _draft.ExpandSuggestionPills,
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

    /// <summary>
    /// Applies the selected preset state to companion rows whose presets have changed.
    /// </summary>
    /// <param name="previous">The companion rows before the update.</param>
    /// <param name="current">The companion rows after the update.</param>
    /// <returns><c>true</c> if any companion preset changed; <c>false</c> otherwise.</returns>
    private bool ApplyCompanionPresetChanges(
        List<CompanionAppFormRow> previous,
        List<CompanionAppFormRow> current)
    {
        var changed = false;
        for (var i = 0; i < current.Count; i++)
        {
            // Rows beyond the previous list are newly added: they have no prior
            // preset to compare against, so always apply their preset's state
            // (path, arguments, launch setting) rather than leaving it blank.
            if (i < previous.Count
                && string.Equals(previous[i].Preset, current[i].Preset, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ApplyCompanionFormState(i, CompanionAppCatalog.CreateStateFromPreset(current[i].Preset));
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Applies the specified companion application state to a draft row.
    /// </summary>
    /// <param name="index">The zero-based index of the companion row to update.</param>
    /// <param name="state">The companion application state to apply.</param>
    /// <param name="persist">Whether to persist the updated draft when the baseline is ready.</param>
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

    /// <summary>
    /// Synchronizes the draft's legacy companion application fields with its companion rows.
    /// </summary>
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

    /// <summary>
    /// Merges companion application values from editor inputs with the existing rows.
    /// </summary>
    /// <param name="data">The input values for companion presets and arguments.</param>
    /// <param name="existing">The current companion application rows.</param>
    /// <returns>The merged companion application rows, containing at least one row.</returns>
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

    /// <summary>
    /// Merges command row values from editor inputs into the existing launch rows.
    /// </summary>
    /// <param name="data">The input values keyed by launch row field and index.</param>
    /// <param name="existing">The current launch rows used for unchanged values and row identity.</param>
    /// <returns>The merged launch rows.</returns>
    private List<LaunchRowDraft> MergeCommandsFromInputs(
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

        var fallbackLaunchTarget = GetDefaultRowLaunchTarget();
        List<LaunchRowDraft> merged = [];
        for (var i = 0; i < count; i++)
        {
            var prior = i < existing.Count ? existing[i] : new();
            var mergedCommand = data[$"LaunchCommand_{i}"]?.ToString() ?? prior.Command;

            // Backspacing a row's command to blank makes the row available for pills again;
            // without this, ApplyPill skips it forever (same as intentionally blank Open to Directory).
            var becameBlankViaEdit = !string.IsNullOrWhiteSpace(prior.Command) && string.IsNullOrWhiteSpace(mergedCommand);

            merged.Add(new LaunchRowDraft
            {
                Id = prior.Id,
                Command = mergedCommand,
                TaskType = TaskTypeCatalog.Normalize(data[$"LaunchType_{i}"]?.ToString() ?? prior.TaskType),
                LaunchTarget = data[$"LaunchTarget_{i}"]?.ToString()
                    ?? prior.LaunchTarget
                    ?? fallbackLaunchTarget,
                RunAsAdmin = ParseToggleBool(data[$"LaunchRunAsAdmin_{i}"]?.ToString(), prior.RunAsAdmin),
                IsEditorPlaceholder = becameBlankViaEdit || prior.IsEditorPlaceholder,
            });
        }

        return merged;
    }

    /// <summary>
    /// Determines the launch target to use for a new command row.
    /// </summary>
    /// <returns>The first command's launch target, the draft launch target, or <c>"default"</c>.</returns>
    private string GetDefaultRowLaunchTarget()
    {
        if (_draft.Commands.Count > 0 && !string.IsNullOrWhiteSpace(_draft.Commands[0].LaunchTarget))
        {
            return _draft.Commands[0].LaunchTarget;
        }

        return string.IsNullOrWhiteSpace(_draft.LaunchTarget) ? "default" : _draft.LaunchTarget;
    }

    /// <summary>
    /// Synchronizes the draft launch target with the first command's launch target.
    /// </summary>
    private void SyncDraftLaunchTargetFromCommands()
    {
        if (_draft.Commands.Count > 0)
        {
            _draft.LaunchTarget = _draft.Commands[0].LaunchTarget;
        }
    }

    /// <summary>
    /// Synchronizes the draft's administrator execution setting with its first command.
    /// </summary>
    private void SyncDraftRunAsAdminFromCommands()
    {
        _draft.RunAsAdmin = _draft.Commands.Count > 0 && _draft.Commands[0].RunAsAdmin;
    }

    /// <summary>
    /// Updates name customization tracking based on the merged name and directory.
    /// </summary>
    /// <param name="mergedName">The name merged into the draft.</param>
    /// <param name="mergedDirectory">
    /// The directory the merged name should be compared against. Callers that also merge a new
    /// directory in the same operation must pass that merged value explicitly rather than relying
    /// on <c>_draft.Directory</c>, which may not be updated yet.
    /// </param>
    private void UpdateAutoFilledNameTracking(string mergedName, string? mergedDirectory = null)
    {
        mergedDirectory ??= _draft.Directory;

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
            && !string.IsNullOrWhiteSpace(mergedDirectory))
        {
            var derived = DeriveNameFromDirectory(mergedDirectory);
            if (string.Equals(
                    Normalize(mergedName),
                    Normalize(derived),
                    StringComparison.OrdinalIgnoreCase))
            {
                _autoFilledName = mergedName;
                _nameCustomized = false;
            }
            else
            {
                _nameCustomized = true;
            }
        }
    }
}
