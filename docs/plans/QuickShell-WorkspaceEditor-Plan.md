\# QuickShell 4.6 Plan — Extract CmdPal Workspace Editor Session



\## Goal



Refactor the CmdPal create/edit workspace flow so `ShortcutFormPage` and its inner form stop acting as a large state machine.



The new design should separate three concerns:



1\. \*\*Workspace editing state and behavior\*\*

&#x20;  Owned by a new per-form `WorkspaceEditor` session.



2\. \*\*CmdPal Adaptive Card rendering and submit handling\*\*

&#x20;  Owned by a thin `ShortcutForm` adapter.



3\. \*\*Page lifetime, navigation, and disposal\*\*

&#x20;  Owned by `ShortcutFormPage`.



The PR must preserve the current in-palette create/edit experience. It should not redesign the UX, change persistence format, rewrite Core, or start a general static-cleanup campaign.



\---



\## Prerequisites



Start only after the relevant Tier-1 DI work is merged, or explicitly rebase onto it.



Assumed already present:



\* `IQuickShellServices` is non-nullable.

\* `QuickShellHostServices` / `QuickShellPageContext` split exists.

\* `IProjectAnalysisService` is threaded through helpers.

\* `ProjectAnalysisAccessor` is gone.



Before coding, verify those assumptions against current `master`. If they are false, stop and report the correct dependency order.



\---



\## Scope



\### In scope



\* Extract create/edit form draft state into a new per-open editor session.

\* Move transient editor state out of `ShortcutFormPage`.

\* Make `ShortcutForm` a thin CmdPal form adapter.

\* Preserve restored drafts, undo/redo, suggestion pills, companion rows, launch rows, directory selection, save/cancel/discard behavior.

\* Remove `ShortcutCreateNavigationState` if it is still only a fallback.

\* Replace `ImportConflictState` only if the resulting diff remains small and directly connected to this form/settings flow.

\* Add lifecycle, disposal, and regression tests.



\### Out of scope



\* Refactoring `ShortcutDetailsFormPage`.

\* Making `QuickShell.Run` reuse the editor.

\* Moving Adaptive Card JSON builders out of Core.

\* Converting every static helper.

\* General service-locator/static cleanup.

\* Redesigning command IDs.

\* Changing persistence formats.

\* Changing workspace JSON schema.

\* Changing import/export semantics except for replacing directly touched static process state.



\---



\## Key design rule



Do \*\*not\*\* conflate rebuilding the CmdPal form with resetting the editor.



Use separate concepts:



```csharp

private bool \_formNeedsRebuild;

private bool \_editorSessionNeedsReset;

```



A form rebuild may refresh `TemplateJson` and `DataJson`.



An editor reset may only happen when opening a new create/edit session.



A template rebuild must never wipe:



\* unsaved inputs;

\* restored draft state;

\* undo/redo history;

\* companion rows;

\* launch rows;

\* suggestion-pill expansion state;

\* in-flight scan cancellation state.



\---



\# Architecture



\## 1. WorkspaceEditor session



Create a CmdPal-layer editor session.



Location:



```text

QuickShell/Services/WorkspaceEditor/

```



Preferred shape:



```csharp

internal interface IWorkspaceEditor : IDisposable

{

&#x20;   WorkspaceEditState GetState();



&#x20;   bool CanUndo { get; }

&#x20;   bool CanRedo { get; }

&#x20;   bool HasUnsavedChanges { get; }

&#x20;   bool IsSuggestionScanning { get; }



&#x20;   event EventHandler<WorkspaceEditChangedEventArgs>? Changed;



&#x20;   void ResetForOpen(

&#x20;       TerminalShortcut? existing,

&#x20;       TerminalShortcut? createSeed);



&#x20;   bool TryApplyInputs(

&#x20;       string payload,

&#x20;       bool excludeDirectory = false);



&#x20;   WorkspaceEditResult SelectDirectory(string directory);

&#x20;   WorkspaceEditResult TryAddSuggestedCommand(string? command, string? taskType, int pillIndex);

&#x20;   WorkspaceEditResult ClearLaunchRow(int index);

&#x20;   WorkspaceEditResult SetExpandSuggestionPills(bool expand);

&#x20;   WorkspaceEditResult RefreshTerminals(

&#x20;       IReadOnlyList<string> availableTargetIds,

&#x20;       string defaultTargetId);



&#x20;   WorkspaceEditResult AddCompanionRow();

&#x20;   WorkspaceEditResult RemoveCompanionRow(int index);

&#x20;   WorkspaceEditResult ApplyCompanionPreset(int index, string preset);

&#x20;   WorkspaceEditResult SetCompanionExecutable(int index, string path);



&#x20;   WorkspaceEditResult Save();

&#x20;   WorkspaceEditResult Cancel();

&#x20;   WorkspaceEditResult Discard();



&#x20;   bool TryUndo();

&#x20;   bool TryRedo();



&#x20;   void LeaveForm();

}

```



But do not manually construct it everywhere. Add a factory:



```csharp

internal interface IWorkspaceEditorFactory

{

&#x20;   IWorkspaceEditor Create(Action? onSaved = null);

}

```



The factory is DI-owned. The editor session is page-owned and disposed by the page.



\---



\## 2. WorkspaceEditState



The editor exposes immutable snapshots to the form.



```csharp

internal sealed record WorkspaceEditState(

&#x20;   string? OriginalName,

&#x20;   string Name,

&#x20;   string Abbreviation,

&#x20;   string Directory,

&#x20;   string LaunchTarget,

&#x20;   string DevServerUrl,

&#x20;   string RepoUrl,

&#x20;   bool OpenDevServerOnLaunch,

&#x20;   bool OpenCompanionAppOnLaunch,

&#x20;   string CompanionAppPreset,

&#x20;   string CompanionAppPath,

&#x20;   string CompanionAppArguments,

&#x20;   IReadOnlyList<LaunchRowDraft> Commands,

&#x20;   IReadOnlyList<CompanionAppFormRow> Companions,

&#x20;   IReadOnlyList<CommandSuggestionPill> Pills,

&#x20;   bool ExpandSuggestionPills,

&#x20;   bool IsSuggestionScanning,

&#x20;   bool ShowRestoredDraftNote,

&#x20;   string? SaveError);

```



Internally, `WorkspaceEditor` may use a mutable model, but outside callers should only receive snapshots.



\---



\## 3. WorkspaceEditResult



The editor should not return CmdPal `CommandResult`.



It should return domain/session results.



```csharp

internal enum WorkspaceEditResultKind

{

&#x20;   StayOpen,

&#x20;   Saved,

&#x20;   Cancelled,

&#x20;   Discarded,

&#x20;   PromptDiscard

}



internal readonly record struct WorkspaceEditResult(

&#x20;   WorkspaceEditResultKind Kind,

&#x20;   string? Message = null)

{

&#x20;   public static WorkspaceEditResult StayOpen(string? message = null) =>

&#x20;       new(WorkspaceEditResultKind.StayOpen, message);



&#x20;   public static WorkspaceEditResult Saved(string? message = null) =>

&#x20;       new(WorkspaceEditResultKind.Saved, message);



&#x20;   public static WorkspaceEditResult Cancelled() =>

&#x20;       new(WorkspaceEditResultKind.Cancelled);



&#x20;   public static WorkspaceEditResult Discarded() =>

&#x20;       new(WorkspaceEditResultKind.Discarded);



&#x20;   public static WorkspaceEditResult PromptDiscard() =>

&#x20;       new(WorkspaceEditResultKind.PromptDiscard);

}

```



`ShortcutForm` maps these to `CommandResult`.



This keeps the editor independent of CmdPal navigation.



\---



\## 4. WorkspaceFormActionParser



Move scattered submit-action detection into one parser.



Location:



```text

QuickShell/Services/WorkspaceEditor/WorkspaceFormActionParser.cs

```



```csharp

internal enum WorkspaceFormActionKind

{

&#x20;   None,

&#x20;   Save,

&#x20;   Discard,

&#x20;   Cancel,

&#x20;   Browse,

&#x20;   Paste,

&#x20;   AddSuggestedCommand,

&#x20;   ClearLaunch,

&#x20;   ExpandSuggestionPills,

&#x20;   CollapseSuggestionPills,

&#x20;   RefreshTerminals,

&#x20;   AddCompanionApp,

&#x20;   RemoveCompanionApp,

&#x20;   BrowseCompanionApp,

&#x20;   ApplyCompanionPreset,

&#x20;   Help

}



internal readonly record struct WorkspaceFormAction(

&#x20;   WorkspaceFormActionKind Kind,

&#x20;   int Index = 0,

&#x20;   string? PillCommand = null,

&#x20;   string? PillTaskType = null,

&#x20;   int PillIndex = -1,

&#x20;   string? Preset = null);



internal static class WorkspaceFormActionParser

{

&#x20;   public static WorkspaceFormAction Parse(string mergedPayload);

&#x20;   public static WorkspaceFormAction ParseDiscardPromptAction(string mergedPayload);

}

```



`FormPayloadMerge` stays where it is.



\---



\# ShortcutFormPage responsibility



`ShortcutFormPage` becomes a page shell.



It owns:



\* existing workspace clone;

\* optional create seed;

\* editor session lifetime;

\* form adapter lifetime;

\* page title/id/icon;

\* disposal.



It does \*\*not\*\* own:



\* draft mutation;

\* undo stack;

\* save logic;

\* suggestion scan logic;

\* companion-row logic;

\* launch-row logic.



Sketch:



```csharp

internal sealed partial class ShortcutFormPage : ContentPage, IDisposable

{

&#x20;   private readonly IWorkspaceEditorFactory \_editorFactory;

&#x20;   private readonly IQuickShellServices \_services;

&#x20;   private readonly TerminalShortcut? \_existing;

&#x20;   private readonly TerminalShortcut? \_createSeed;

&#x20;   private readonly Action? \_onSaved;

&#x20;   private readonly object \_sync = new();



&#x20;   private IWorkspaceEditor? \_editor;

&#x20;   private ShortcutForm? \_form;

&#x20;   private bool \_formNeedsRebuild;

&#x20;   private bool \_editorSessionNeedsReset;

&#x20;   private bool \_disposed;



&#x20;   public override IContent\[] GetContent()

&#x20;   {

&#x20;       lock (\_sync)

&#x20;       {

&#x20;           ObjectDisposedException.ThrowIf(\_disposed, this);



&#x20;           var editor = EnsureEditor();



&#x20;           if (\_form is null || \_formNeedsRebuild)

&#x20;           {

&#x20;               \_form?.Dispose();

&#x20;               \_form = new ShortcutForm(

&#x20;                   editor,

&#x20;                   \_services,

&#x20;                   requestFormRebuild: () => \_formNeedsRebuild = true,

&#x20;                   onSaved: \_onSaved);



&#x20;               \_formNeedsRebuild = false;

&#x20;           }



&#x20;           return \[\_form];

&#x20;       }

&#x20;   }



&#x20;   private IWorkspaceEditor EnsureEditor()

&#x20;   {

&#x20;       if (\_editor is null)

&#x20;       {

&#x20;           \_editor = \_editorFactory.Create(\_onSaved);

&#x20;           \_editor.ResetForOpen(\_existing, \_createSeed);

&#x20;           \_editorSessionNeedsReset = false;

&#x20;       }

&#x20;       else if (\_editorSessionNeedsReset)

&#x20;       {

&#x20;           \_editor.ResetForOpen(\_existing, \_createSeed);

&#x20;           \_editorSessionNeedsReset = false;

&#x20;       }



&#x20;       return \_editor;

&#x20;   }



&#x20;   public override void Dispose()

&#x20;   {

&#x20;       if (\_disposed)

&#x20;       {

&#x20;           return;

&#x20;       }



&#x20;       \_disposed = true;

&#x20;       \_form?.Dispose();

&#x20;       \_editor?.Dispose();

&#x20;       base.Dispose();

&#x20;       GC.SuppressFinalize(this);

&#x20;   }

}

```



Exact implementation may differ, but the separation must hold.



\---



\# ShortcutForm responsibility



`ShortcutForm` becomes a thin `FormContent` adapter.



It owns:



\* `TemplateJson`;

\* `DataJson`;

\* submit dispatch;

\* picker/clipboard actions;

\* mapping editor results to CmdPal command results.



It does \*\*not\*\* own:



\* editor state;

\* draft baseline;

\* undo stack;

\* save/upsert;

\* suggestion scan lifecycle.



Submit flow:



1\. Merge payload.

2\. Parse action.

3\. For picker actions, apply current inputs first.

4\. Call the relevant editor method.

5\. Map `WorkspaceEditResult` to `CommandResult`.

6\. Rebuild data/template from `WorkspaceEditState`.



Important: `ShortcutForm` may call OS/UI helpers like folder picker or clipboard. That remains host/UI behavior and should not move into `WorkspaceEditor`.



\---



\# Background scan and event safety



`WorkspaceEditor` may schedule suggestion scans, but it must not directly rebuild CmdPal UI from a worker thread.



Required behavior:



\* Per-open editor session owns a scan `CancellationTokenSource`.

\* It links to `IQuickShellLifetime.CancellationToken`.

\* `ResetForOpen` cancels any previous scan and creates a new per-open token.

\* `LeaveForm` cancels current scan.

\* `Dispose` cancels and disposes current scan token.

\* `Changed` is raised only after safe handoff to the extension callback queue, or the subscriber performs that handoff before touching CmdPal state.

\* No `Changed` event may call into a disposed form/page.

\* Late scan completion after disposal is ignored.



Add tests for late completion and disposal.



\---



\# Draft and undo behavior



`WorkspaceEditor` owns:



\* restored draft detection;

\* draft baseline;

\* dirty tracking;

\* form-local undo/redo;

\* state snapshots;

\* draft persistence after mutations.



Preserve existing semantics:



\* Existing workspace edit can restore in-progress draft.

\* Create seed opens without relying on a static fallback.

\* Dirty cancel prompts discard.

\* Clean cancel goes back.

\* Save validates, upserts, clears draft, and reports success.

\* Undo/redo covers launch rows, companion rows, fields, and suggestion expansion.

\* Draft save triggers remain equivalent to current behavior.



`FormEditHistory<WorkspaceEditSnapshot>` may remain in Core if it is pure and UI-agnostic.



\---



\# Static cleanup in this PR



\## 1. Delete ShortcutCreateNavigationState



Only do this if the audit confirms:



\* `SetSeed` has no meaningful callers;

\* `TryTakeSeed` is only a fallback inside `ShortcutFormPage`;

\* seeds already flow through constructors.



Change:



```text

createSeed ?? ShortcutCreateNavigationState.TryTakeSeed()

```



to:



```text

createSeed

```



Then delete the file and tests that only validate the static fallback.



\## 2. Replace ImportConflictState only if bounded



Preferred replacement:



```csharp

internal interface IImportConflictService

{

&#x20;   bool HasPending { get; }

&#x20;   PendingImport? Pending { get; }



&#x20;   void Set(

&#x20;       ImportTransferKind kind,

&#x20;       string path,

&#x20;       int conflictCount,

&#x20;       int importCount);



&#x20;   void Clear();



&#x20;   bool TryAbandonPending(out string message);



&#x20;   event Action? Changed;

}

```



Register as a singleton in the host composition root.



Keep this service in the CmdPal host layer unless Core genuinely needs it. Import-conflict UI state is host/session state, not domain state.



Update only direct callers:



\* import command;

\* import conflict page;

\* transfer/settings form;

\* reset projects command;

\* any still-live navigation helper.



Remove dead navigation helpers only if they are truly unused.



Do not expand this into a general navigation refactor.



\---



\# Service/context changes



Avoid turning `IQuickShellServices` into a new service locator.



Preferred order:



1\. Put `IWorkspaceEditorFactory` in DI.

2\. Inject the factory only where `ShortcutFormPage` is created.

3\. Expose `ImportConflicts` only on the narrow context that needs it.

4\. Add to `IQuickShellServices` only if several existing commands/pages already depend on the shared facade and a narrower context would increase churn.



Document the decision in the PR description.



\---



\# Tests



Add tests for the editor session and adapter behavior at the correct layer.



Do not move host/session code into Core merely to test it.



\## WorkspaceEditor tests



Cover:



\* `ResetForOpen` with existing workspace.

\* `ResetForOpen` with create seed.

\* restored draft behavior.

\* `TryApplyInputs` updates fields.

\* launch rows apply correctly.

\* companion rows apply correctly.

\* `SelectDirectory` updates directory and expected derived hints.

\* `TryAddSuggestedCommand` updates rows and undo state.

\* `ClearLaunchRow` preserves minimum row behavior.

\* `SetExpandSuggestionPills` participates in undo/redo.

\* `Save` validates, upserts, clears draft, and signals saved result.

\* `Cancel` prompts when dirty.

\* `Cancel` exits when clean.

\* `Discard` clears draft/session state.

\* `LeaveForm` cancels scans.

\* `Dispose` unsubscribes and cancels work.

\* late suggestion scan completion does not call disposed subscribers.



\## ShortcutForm adapter tests



Cover:



\* save action maps to `CommandResult.GoBack`.

\* discard prompt maps save/discard correctly.

\* browse action applies inputs before selecting directory.

\* paste action handles invalid clipboard path without corrupting state.

\* refresh terminals invalidates/reloads choices without resetting editor.

\* unknown action stays open safely.

\* parse failure shows read-error toast and preserves state.



\## Static cleanup tests



Cover:



\* no `ShortcutCreateNavigationState` fallback remains.

\* import conflicts are process/provider-owned through service instance.

\* clearing import conflict raises `Changed`.

\* abandoning pending import returns the expected message.

\* settings form/page updates when conflict state changes.



\## Architecture/regression tests



Add guards for this slice only:



\* no `ShortcutCreateNavigationState`;

\* no `ImportConflictState`;

\* `ShortcutFormPage` does not contain large draft/state-machine methods;

\* `ShortcutForm` does not own `FormEditHistory`;

\* no new `QuickShellServices.Current`;

\* no new static mutable form/edit state.



\---



\# Validation



Run:



```powershell

dotnet restore QuickShell.sln

dotnet build QuickShell.sln -c Release -p:Platform=x64 --no-restore -warnaserror

dotnet test QuickShell.Core.Tests/QuickShell.Core.Tests.csproj -c Release -p:Platform=x64

dotnet format QuickShell.sln --verify-no-changes

git diff --check

git status --short --untracked-files=all

```



Also run any new QuickShell host test project if one exists or is added.



If a command fails:



1\. capture output;

2\. run the same command on clean `master`;

3\. compare;

4\. report whether the failure is introduced or pre-existing.



\---



\# PR delivery



Branch name:



```text

refactor/cmdpal-workspace-editor-session

```



Draft PR title:



```text

Refactor CmdPal workspace form into editor session

```



PR description must include:



\* current problem;

\* new editor/session ownership model;

\* what remains in `ShortcutFormPage`;

\* what remains in `ShortcutForm`;

\* what moved to `WorkspaceEditor`;

\* static state removed;

\* lifecycle/disposal behavior;

\* background scan/threading behavior;

\* tests added;

\* validation commands and results;

\* explicit out-of-scope follow-ups.



\---



# Follow-ups



Do not include these in this PR:



1. Move Adaptive Card JSON builders from Core to QuickShell.

2. ~~Evaluate whether `QuickShell.Run` should share a UI-agnostic edit session.~~ **Done:** `IWorkspaceEditor` lives in Core; Run's one-page WPF window binds via `TryApplyHostFields` / `Save`.

3. Refactor `ShortcutDetailsFormPage`.

4. Finish broader static-service cleanup.

5. Consolidate command ID factories.

6. Split companion/suggestion providers into DI registries.



\---



\# Acceptance criteria



The PR is merge-ready only when:



\* create workspace works;

\* edit workspace works;

\* restored drafts still work;

\* undo/redo still works;

\* save/cancel/discard still work;

\* launch rows still work;

\* companion rows still work;

\* suggestion pills still work;

\* directory browse/paste still work;

\* terminal refresh still works;

\* dirty cancel prompts;

\* clean cancel exits;

\* provider disposal cancels editor work;

\* no late background scan updates disposed UI;

\* `ShortcutCreateNavigationState` is gone or explicitly justified;

\* `ImportConflictState` is gone or explicitly deferred;

\* no new static mutable editor state exists;

\* validation passes or failures are proven pre-existing.

