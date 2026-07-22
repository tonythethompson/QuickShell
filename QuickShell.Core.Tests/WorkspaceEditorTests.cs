using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Core.Services;
using QuickShell.Models;
using QuickShell.Services;
using QuickShell.Services.WorkspaceEditor;

namespace QuickShell.Core.Tests;

public sealed class WorkspaceEditorTests : IDisposable
{
    private readonly QuickShellLifetime _lifetime = new();
    private readonly TempDataDirectory _temp = new();
    private readonly FakeShortcutRepository _repository;
    private readonly ShortcutDraftStore _drafts;
    private readonly FakeProjectAnalysisService _analysis = new();
    private readonly ICommandSuggestionService _commandSuggestions;

    public WorkspaceEditorTests()
    {
        _repository = new FakeShortcutRepository([], _temp.Path);
        _drafts = new ShortcutDraftStore(_repository);
        _commandSuggestions = new CommandSuggestionService([]);
    }

    public void Dispose()
    {
        _lifetime.Dispose();
        _drafts.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public void ResetForOpen_CreateSeed_LoadsValuesIntoState()
    {
        var editor = CreateEditor();
        editor.ResetForOpen(null, new TerminalShortcut
        {
            Name = "SeedProject",
            Directory = _temp.Path,
        });

        var state = editor.GetState();
        Assert.Equal("SeedProject", state.Name);
        Assert.Equal(_temp.Path, state.Directory);
        Assert.False(editor.HasUnsavedChanges);
    }

    [Fact]
    public void TryApplyInputs_UpdatesNameAndRaisesChanged()
    {
        var editor = CreateEditor();
        editor.ResetForOpen(null, new TerminalShortcut { Directory = _temp.Path });

        var changedCount = 0;
        editor.Changed += (_, _) => changedCount++;

        var applied = editor.TryApplyInputs("""{"Name":"New Name"}""");

        Assert.True(applied);
        Assert.Equal("New Name", editor.GetState().Name);
        Assert.True(editor.HasUnsavedChanges);
        Assert.True(changedCount > 0);
    }

    [Fact]
    public void UndoRedo_RestoresAndReappliesCompanionRowCount()
    {
        var editor = CreateEditor();
        editor.ResetForOpen(null, new TerminalShortcut { Directory = _temp.Path, Name = "Project" });

        var before = editor.GetState().Companions.Count;
        editor.AddCompanionRow();
        Assert.Equal(before + 1, editor.GetState().Companions.Count);

        Assert.True(editor.CanUndo);
        Assert.True(editor.TryUndo());
        Assert.Equal(before, editor.GetState().Companions.Count);

        Assert.True(editor.CanRedo);
        Assert.True(editor.TryRedo());
        Assert.Equal(before + 1, editor.GetState().Companions.Count);
    }

    [Fact]
    public void Save_ValidNewWorkspace_PersistsAndInvokesOnSaved()
    {
        var called = false;
        var editor = CreateEditor(() => called = true);
        editor.ResetForOpen(null, new TerminalShortcut { Directory = _temp.Path });

        var payload = "{" +
            "\"Name\": \"SavedProject\"," +
            "\"Directory\": \"" + JsonEscaped(_temp.Path) + "\"," +
            "\"LaunchCommand_0\": \"npm start\"" +
            "}";
        editor.TryApplyInputs(payload);

        var result = editor.Save();

        Assert.Equal(WorkspaceEditResultKind.Saved, result.Kind);
        Assert.True(called);
        var saved = _repository.GetByName("SavedProject");
        Assert.NotNull(saved);
        Assert.Equal(_temp.Path, saved!.Directory);
    }

    [Fact]
    public void Cancel_WithUnsavedChanges_PromptsDiscard()
    {
        var editor = CreateEditor();
        editor.ResetForOpen(null, new TerminalShortcut { Directory = _temp.Path, Name = "Original" });
        editor.TryApplyInputs("""{"Name":"Changed"}""");

        var result = editor.Cancel();

        Assert.Equal(WorkspaceEditResultKind.PromptDiscard, result.Kind);
    }

    [Fact]
    public void Discard_AfterPrompt_ReturnsDiscarded()
    {
        var editor = CreateEditor();
        editor.ResetForOpen(null, new TerminalShortcut { Directory = _temp.Path, Name = "Original" });
        editor.TryApplyInputs("""{"Name":"Changed"}""");
        editor.Cancel();

        var result = editor.Discard();

        Assert.Equal(WorkspaceEditResultKind.Discarded, result.Kind);
        Assert.False(editor.HasUnsavedChanges);
    }

    [Fact]
    public void SelectDirectory_AutofillsNameFromFolder()
    {
        var editor = CreateEditor();
        editor.ResetForOpen(null, null);

        var projectDir = Path.Join(_temp.Path, "KnownProject");
        Directory.CreateDirectory(projectDir);
        editor.SelectDirectory(projectDir);

        Assert.Equal("KnownProject", editor.GetState().Name);
    }

    [Fact]
    public void AddCompanionRow_IncreasesCompanionCount()
    {
        var editor = CreateEditor();
        editor.ResetForOpen(null, new TerminalShortcut { Directory = _temp.Path });

        var before = editor.GetState().Companions.Count;
        editor.AddCompanionRow();

        Assert.Equal(before + 1, editor.GetState().Companions.Count);
    }

    [Fact]
    public void LeaveForm_DoesNotThrowAfterActiveSession()
    {
        var editor = CreateEditor();
        editor.ResetForOpen(null, new TerminalShortcut { Directory = _temp.Path });
        editor.TryApplyInputs("""{"Name":"Leaving"}""");

        var exception = Record.Exception(() => editor.LeaveForm());

        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_DoesNotThrowAfterActiveSession()
    {
        var editor = CreateEditor();
        editor.ResetForOpen(null, new TerminalShortcut { Directory = _temp.Path });

        var exception = Record.Exception(() => editor.Dispose());

        Assert.Null(exception);
    }

    [Fact]
    public void TryApplyInputs_TogglingOpenDevServerOnLaunch_MarksUnsavedChanges()
    {
        var editor = CreateEditor();
        editor.ResetForOpen(null, new TerminalShortcut { Directory = _temp.Path, Name = "Project" });
        Assert.False(editor.HasUnsavedChanges);

        editor.TryApplyInputs("""{"OpenDevServerOnLaunch":"true"}""");

        Assert.True(editor.HasUnsavedChanges);

        var result = editor.Cancel();

        Assert.Equal(WorkspaceEditResultKind.PromptDiscard, result.Kind);
    }

    [Fact]
    public void ResetForOpen_RestoredEditDraft_KeepsSavedBaselineForDiscardPrompt()
    {
        var existing = new TerminalShortcut
        {
            Id = "restore-baseline",
            Name = "Original",
            Directory = _temp.Path,
            Command = "npm start",
        };
        _repository.Upsert(existing);

        using (var first = CreateEditor())
        {
            first.ResetForOpen(existing, null);
            Assert.True(first.TryApplyInputs("""{"Name":"RestoredEdit"}"""));
            first.LeaveForm();
        }

        using var editor = CreateEditor();
        editor.ResetForOpen(existing, null);

        Assert.Equal("RestoredEdit", editor.GetState().Name);
        Assert.True(editor.GetState().ShowRestoredDraftNote);
        Assert.True(editor.HasUnsavedChanges);

        var result = editor.Cancel();

        Assert.Equal(WorkspaceEditResultKind.PromptDiscard, result.Kind);
    }

    [Fact]
    public void UndoRedo_RestoresCompanionPresetChange()
    {
        var editor = CreateEditor();
        editor.ResetForOpen(null, new TerminalShortcut { Directory = _temp.Path, Name = "Project" });

        var before = editor.GetState().Companions[0].Preset;
        editor.ApplyCompanionPreset(0, CompanionAppCatalog.PresetExplorer);
        Assert.Equal(CompanionAppCatalog.PresetExplorer, editor.GetState().Companions[0].Preset);

        Assert.True(editor.CanUndo);
        Assert.True(editor.TryUndo());
        Assert.Equal(before, editor.GetState().Companions[0].Preset);

        Assert.True(editor.CanRedo);
        Assert.True(editor.TryRedo());
        Assert.Equal(CompanionAppCatalog.PresetExplorer, editor.GetState().Companions[0].Preset);
    }

    [Fact]
    public void TryApplyHostFields_UpdatesScalarsAndMarksDirty()
    {
        var editor = CreateEditor();
        editor.ResetForOpen(null, new TerminalShortcut { Directory = _temp.Path, Name = "Original" });
        Assert.False(editor.HasUnsavedChanges);

        var applied = editor.TryApplyHostFields(new WorkspaceHostFieldUpdate
        {
            Name = "HostName",
            Abbreviation = "hn",
            Directory = _temp.Path,
            DevServerUrl = "http://localhost:3000",
            OpenDevServerOnLaunch = true,
            RepoUrl = "https://example.com/repo.git",
            Commands = editor.GetState().Commands.Select(c => c.Clone()).ToList(),
            Companions = editor.GetState().Companions.Select(c => c.Clone()).ToList(),
        });

        Assert.True(applied);
        var state = editor.GetState();
        Assert.Equal("HostName", state.Name);
        Assert.Equal("hn", state.Abbreviation);
        Assert.Equal("http://localhost:3000", state.DevServerUrl);
        Assert.True(state.OpenDevServerOnLaunch);
        Assert.Equal("https://example.com/repo.git", state.RepoUrl);
        Assert.True(editor.HasUnsavedChanges);
    }

    [Fact]
    public void LaunchRowOperations_ParticipateInUndoAndPreventDuplicateTerminalOnly()
    {
        var editor = CreateEditor();
        editor.ResetForOpen(null, new TerminalShortcut { Directory = _temp.Path, Name = "Project" });

        editor.AddOpenInTerminalRow();
        editor.AddOpenInTerminalRow();
        Assert.Equal(LaunchRowKind.OpenInTerminal, Assert.Single(editor.GetState().Commands).Kind);
        Assert.True(editor.TryApplyInputs("""{"LaunchKind_0":"OpenInTerminal","LaunchLabel_0":"Terminal","LaunchIsEnabled_0":"false","LaunchTarget_0":"wt:pwsh","LaunchRunAsAdmin_0":"true"}"""));

        editor.RemoveLaunchRow(0);
        Assert.Empty(editor.GetState().Commands);
        Assert.True(editor.TryUndo());
        var restored = Assert.Single(editor.GetState().Commands);
        Assert.Equal(LaunchRowKind.OpenInTerminal, restored.Kind);
        Assert.Equal("Terminal", restored.Label);
        Assert.False(restored.IsEnabled);
        Assert.Equal("wt:pwsh", restored.LaunchTarget);
        Assert.True(restored.RunAsAdmin);
    }

    [Fact]
    public void AddOpenInTerminalRow_InsertsAtFrontOfExistingCommands()
    {
        var editor = CreateEditor();
        editor.ResetForOpen(null, new TerminalShortcut { Directory = _temp.Path, Name = "Project" });

        editor.AddCommandRow();
        Assert.True(editor.TryApplyInputs("""{"LaunchKind_0":"Command","LaunchLabel_0":"Dev","LaunchCommand_0":"npm start","LaunchIsEnabled_0":"true"}"""));
        editor.AddOpenInTerminalRow();

        var state = editor.GetState();
        Assert.Equal(2, state.Commands.Count);
        Assert.Equal(LaunchRowKind.OpenInTerminal, state.Commands[0].Kind);
        Assert.Equal(LaunchRowKind.Command, state.Commands[1].Kind);
        Assert.Equal("npm start", state.Commands[1].Command);
    }

    [Fact]
    public void Save_WithoutRealLaunchesFailsWithActionableMessage()
    {
        var editor = CreateEditor();
        editor.ResetForOpen(null, new TerminalShortcut { Directory = _temp.Path, Name = "Project" });

        var result = editor.Save();

        Assert.Equal(WorkspaceEditResultKind.StayOpen, result.Kind);
        Assert.Equal("Add at least one launch.", result.Message);
    }

    [Fact]
    public void TryApplyInputs_PreservesKindLabelEnabledAndExpandSuggestionPills()
    {
        var editor = CreateEditor();
        editor.ResetForOpen(null, new TerminalShortcut { Directory = _temp.Path, Name = "Project" });
        editor.AddOpenInTerminalRow();
        editor.SetExpandSuggestionPills(true);

        Assert.True(editor.TryApplyInputs("""{"LaunchCommand_0":"","LaunchKind_0":"OpenInTerminal","LaunchLabel_0":"Terminal","LaunchIsEnabled_0":"false"}"""));

        var state = editor.GetState();
        var row = Assert.Single(state.Commands);
        Assert.Equal(LaunchRowKind.OpenInTerminal, row.Kind);
        Assert.Equal("Terminal", row.Label);
        Assert.False(row.IsEnabled);
        Assert.True(state.ExpandSuggestionPills);
    }

    [Fact]
    public void TryApplyHostFields_PreservesKindsAndDropsOnlyPlaceholderOnSave()
    {
        var editor = CreateEditor();
        editor.ResetForOpen(null, new TerminalShortcut { Directory = _temp.Path, Name = "RunProject" });

        Assert.True(editor.TryApplyHostFields(new WorkspaceHostFieldUpdate
        {
            Name = "RunProject",
            Directory = _temp.Path,
            Commands =
            [
                new() { Id = "command", Kind = LaunchRowKind.Command, Label = "Dev", Command = "npm start", LaunchTarget = "wt:pwsh", IsEnabled = true },
                new() { Id = "terminal", Kind = LaunchRowKind.OpenInTerminal, Label = "Shell", LaunchTarget = TerminalCatalog.SameAsPreviousLaunchTargetId, RunAsAdmin = true, IsEnabled = false },
                new() { Id = "placeholder", Kind = LaunchRowKind.Command, Label = "Launch 3", LaunchTarget = TerminalCatalog.SameAsPreviousLaunchTargetId, IsEditorPlaceholder = true },
            ],
            Companions = editor.GetState().Companions,
        }));

        var state = editor.GetState();
        Assert.Equal([LaunchRowKind.Command, LaunchRowKind.OpenInTerminal, LaunchRowKind.Command], state.Commands.Select(row => row.Kind));
        Assert.Equal("Shell", state.Commands[1].Label);
        Assert.False(state.Commands[1].IsEnabled);

        var result = editor.Save();

        Assert.Equal(WorkspaceEditResultKind.Saved, result.Kind);
        var saved = Assert.IsType<TerminalShortcut>(_repository.GetByName("RunProject"));
        Assert.Equal(2, saved.Launches.Count);
        Assert.Equal("npm start", saved.Launches[0].Command);
        Assert.Null(saved.Launches[1].Command);
        Assert.Equal("Shell", saved.Launches[1].Label);
        Assert.False(saved.Launches[1].IsEnabled);
    }

    [Fact]
    public void RestoreLegacyDraft_InfersOnlySavedBlankLaunchAsOpenInTerminal()
    {
        var existing = new TerminalShortcut
        {
            Id = "legacy-kind",
            Name = "Legacy",
            Directory = _temp.Path,
            Launches =
            [
                new WorkspaceEntry { Id = "saved-terminal", Label = "Shell", Command = null, Order = 0 },
            ],
        };
        _repository.Upsert(existing);
        var baseline = new ShortcutFormDraftData
        {
            OriginalName = existing.Name,
            Name = existing.Name,
            Directory = existing.Directory,
            Launches = [new() { Id = "saved-terminal", Label = "Shell", Command = string.Empty }],
        };
        var dirty = new ShortcutFormDraftData
        {
            OriginalName = existing.Name,
            Name = "Legacy restored",
            Directory = existing.Directory,
            Launches =
            [
                new() { Id = "saved-terminal", Label = "Shell", Command = string.Empty },
                new() { Id = "abandoned", Label = "Command 2", Command = string.Empty },
            ],
        };
        _drafts.SaveIfDirty(existing.Name, dirty, baseline, nameCustomized: true, autoFilledName: null);

        using var editor = CreateEditor();
        editor.ResetForOpen(existing, null);

        Assert.Equal(LaunchRowKind.OpenInTerminal, editor.GetState().Commands[0].Kind);
        Assert.Equal(LaunchRowKind.Command, editor.GetState().Commands[1].Kind);
    }

    [Fact]
    public void RestoreLegacyScalarCommand_CreatesCommandRowInZeroRowEditor()
    {
        var existing = new TerminalShortcut { Name = "Scalar", Directory = _temp.Path };
        _repository.Upsert(existing);
        var baseline = new ShortcutFormDraftData
        {
            OriginalName = existing.Name,
            Name = existing.Name,
            Directory = existing.Directory,
        };
        var dirty = new ShortcutFormDraftData
        {
            OriginalName = existing.Name,
            Name = existing.Name,
            Directory = existing.Directory,
            Command = "npm start",
        };
        _drafts.SaveIfDirty(existing.Name, dirty, baseline, nameCustomized: false, autoFilledName: null);

        using var editor = CreateEditor();
        editor.ResetForOpen(existing, null);

        var row = Assert.Single(editor.GetState().Commands);
        Assert.Equal(LaunchRowKind.Command, row.Kind);
        Assert.Equal("npm start", row.Command);
    }

    private WorkspaceEditor CreateEditor(Action? onSaved = null) =>
        new WorkspaceEditor(
            _repository,
            _drafts,
            _analysis,
            _commandSuggestions,
            _lifetime,
            "Add at least one launch.",
            "Open in terminal",
            onSaved);

    private static string JsonEscaped(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private sealed class TempDataDirectory : IDisposable
    {
        public TempDataDirectory()
        {
            Path = System.IO.Path.Join(
                System.IO.Path.GetTempPath(),
                "quickshell-workspace-editor-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (System.IO.IOException)
            {
                // Best-effort cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup.
            }
        }
    }
}
