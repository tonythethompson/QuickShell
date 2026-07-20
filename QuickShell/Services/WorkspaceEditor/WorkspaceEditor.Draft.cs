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
        // Keep the saved-shortcut baseline so restored edits still count as unsaved.
        OnChanged();
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
}
