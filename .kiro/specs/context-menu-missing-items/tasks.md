# Implementation Plan

- [x] 1. Write bug condition exploration test
  - **Property 1: Bug Condition** - Home Pin Context Menu Missing Management Commands
  - **CRITICAL**: This test MUST FAIL on unfixed code - failure confirms the bug exists
  - **DO NOT attempt to fix the test or the code when it fails**
  - **NOTE**: This test encodes the expected behavior - it will validate the fix when it passes after implementation
  - **GOAL**: Surface counterexamples that demonstrate the bug exists
  - **Scoped PBT Approach**: Scope the property to concrete failing cases: BuildForHomePin() called with any non-repair shortcut should include Favorite, Duplicate, Delete, and multi-launch entries
  - Test that `BuildForHomePin()` with a single-launch shortcut does NOT contain Favorite, Duplicate, or Delete commands (will fail = confirms bug)
  - Test that `BuildForHomePin()` with a multi-launch shortcut (2+ enabled launches) does NOT contain individual `OpenShortcutLaunchCommand` entries (will fail = confirms bug)
  - Test that `BuildForHomePin()` with `IsPinned=true` does NOT contain a toggle favorite command with correct text (will fail = confirms bug)
  - Test that `BuildForHomePin()` with any shortcut does NOT contain a Delete command marked `IsCritical=true` (will fail = confirms bug)
  - Assert expected behavior: result contains `ToggleFavoriteShortcutCommand` with Ctrl+F, `DuplicateShortcutCommand` with Ctrl+Shift+D, `DeleteShortcutCommand` with Ctrl+Delete (isCritical), and `OpenShortcutLaunchCommand` entries when enabledLaunches > 1
  - Run test on UNFIXED code
  - **EXPECTED OUTCOME**: Test FAILS (this is correct - it proves the bug exists)
  - Document counterexamples found: BuildForHomePin() returns items array missing Favorite/Duplicate/Delete/multi-launch entries entirely
  - Mark task complete when test is written, run, and failure is documented
  - _Requirements: 1.1, 1.2, 1.3, 1.4_

- [x] 2. Write preservation property tests (BEFORE implementing fix)
  - **Property 2: Preservation** - Existing Commands and Exclusions Unchanged
  - **IMPORTANT**: Follow observation-first methodology
  - Observe: `Build()` output for various shortcut configurations on unfixed code — capture exact command types, ordering, keyboard shortcuts, critical flags
  - Observe: `BuildRepairOnly()` output for repair-needing shortcuts on unfixed code — capture minimal menu structure
  - Observe: `BuildForHomePin()` already includes elevation, folder/link commands, workspace status, edit — verify these are present
  - Write property-based tests:
    - For all shortcut configurations, `Build()` produces identical output (same command types, same order, same shortcuts) before and after fix
    - For all repair-needing shortcuts, `BuildRepairOnly()` produces identical output before and after fix
    - For all shortcuts, `BuildForHomePin()` never contains Undo/Redo commands (`UndoShortcutCommand`, `RedoShortcutCommand`)
    - For all shortcuts, `BuildForHomePin()` never contains pinned move commands (`MoveFavoriteShortcutCommand`)
    - For all shortcuts, `BuildForHomePin()` continues to contain elevation toggle, folder/link, status, and Edit commands
  - Generate random `TerminalShortcut` configurations (varying launch counts, IsPinned, DevServerUrl, RepoUrl, CompanionAppPath) for stronger guarantees
  - Run tests on UNFIXED code
  - **EXPECTED OUTCOME**: Tests PASS (this confirms baseline behavior to preserve)
  - Mark task complete when tests are written, run, and passing on unfixed code
  - _Requirements: 2.5, 2.6, 3.1, 3.2, 3.3, 3.4, 3.5_

- [x] 3. Fix for context menu missing Favorite, Duplicate, Delete, and multi-launch entries in BuildForHomePin()

  - [x] 3.1 Add multi-launch entries to BuildForHomePin() before elevation
    - After the repair check and before `AddElevationContextCommand`, add multi-launch logic:
    - Call `ShortcutLaunchNormalization.GetLaunchesForDisplay(shortcut)` to get enabled launches
    - If `enabledLaunches.Count > 1`, iterate and add `OpenShortcutLaunchCommand` entries with title from `ShortcutDisplay.GetLaunchContextMenuTitle` and icon from `TerminalLaunchGlyphs.GetForLaunch`
    - _Bug_Condition: isBugCondition(input) where input.source == "HomePin" AND input.shortcut.enabledLaunchCount > 1 AND input.expectedCommand == "IndividualLaunch"_
    - _Expected_Behavior: result contains OpenShortcutLaunchCommand entries matching enabledLaunchCount_
    - _Preservation: Build() multi-launch logic unchanged; no multi-launch entries added when count <= 1_
    - _Requirements: 2.4_

  - [x] 3.2 Add Favorite/Unfavorite command after Edit entry
    - Create `ToggleFavoriteShortcutCommand(shortcut.Name, onChanged, shortcut.IsPinned)`
    - Add with `WithShortcut()` using ctrl:true, alt:false, shift:false, VirtualKey.F
    - Set `showInHoverActions: true`, `hoverOrder: HoverOrderFavorite`
    - _Bug_Condition: isBugCondition(input) where input.source == "HomePin" AND input.expectedCommand IN ["Favorite", "Unfavorite"]_
    - _Expected_Behavior: result contains ToggleFavoriteShortcutCommand with Ctrl+F shortcut_
    - _Preservation: Build() Favorite command unchanged; BuildRepairOnly() unaffected_
    - _Requirements: 2.1_

  - [x] 3.3 Add Duplicate command after Favorite
    - Create `DuplicateShortcutCommand(shortcut, onChanged)`
    - Add with `WithShortcut()` using ctrl:true, alt:false, shift:true, VirtualKey.D
    - Set `showInHoverActions: true`, `hoverOrder: HoverOrderDuplicate`
    - _Bug_Condition: isBugCondition(input) where input.source == "HomePin" AND input.expectedCommand == "Duplicate"_
    - _Expected_Behavior: result contains DuplicateShortcutCommand with Ctrl+Shift+D shortcut_
    - _Preservation: Build() Duplicate command unchanged; BuildRepairOnly() unaffected_
    - _Requirements: 2.2_

  - [x] 3.4 Add Delete command after Duplicate
    - Create `DeleteShortcutCommand(shortcut.Name, onChanged)`
    - Add with `WithShortcut()` using ctrl:true, alt:false, shift:false, VirtualKey.Delete
    - Set `isCritical: true`, `showInHoverActions: true`, `hoverOrder: HoverOrderDelete`
    - _Bug_Condition: isBugCondition(input) where input.source == "HomePin" AND input.expectedCommand == "Delete"_
    - _Expected_Behavior: result contains DeleteShortcutCommand with Ctrl+Delete shortcut, isCritical=true_
    - _Preservation: Build() Delete command unchanged; BuildRepairOnly() unaffected_
    - _Requirements: 2.3_

  - [x] 3.5 Verify bug condition exploration test now passes
    - **Property 1: Expected Behavior** - Home Pin Context Menu Includes All Management Commands
    - **IMPORTANT**: Re-run the SAME test from task 1 - do NOT write a new test
    - The test from task 1 encodes the expected behavior
    - When this test passes, it confirms the expected behavior is satisfied
    - Run bug condition exploration test from step 1
    - **EXPECTED OUTCOME**: Test PASSES (confirms bug is fixed)
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [x] 3.6 Verify preservation tests still pass
    - **Property 2: Preservation** - Existing Commands and Exclusions Unchanged
    - **IMPORTANT**: Re-run the SAME tests from task 2 - do NOT write new tests
    - Run preservation property tests from step 2
    - **EXPECTED OUTCOME**: Tests PASS (confirms no regressions)
    - Confirm Build() output unchanged, BuildRepairOnly() output unchanged, no Undo/Redo or move commands in BuildForHomePin()

- [x] 4. Checkpoint - Ensure all tests pass
  - Run full test suite to ensure all property-based tests and unit tests pass
  - Verify on Windows (tests require net10.0-windows10.0.26100.0 and CsWinRT)
  - Ensure no regressions in Build() or BuildRepairOnly() behavior
  - Ask the user if questions arise
