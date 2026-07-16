# Context Menu Missing Items Bugfix Design

## Overview

The `BuildForHomePin()` method in `ShortcutContextCommands.cs` was intentionally created as a lighter context menu builder for CmdPal-pinned items on the QuickShell home page. However, it went too far — it omits Favorite/Unfavorite, Duplicate, Delete, and multi-launch entries that should be universal across all non-repair context menus. The fix adds these missing commands to `BuildForHomePin()` while preserving its intentional exclusions (Undo/Redo and pinned move commands).

## Glossary

- **Bug_Condition (C)**: A shortcut is displayed on the QuickShell home page (CmdPal pinned item) and the user opens its context menu — the menu is missing Favorite, Duplicate, Delete, and multi-launch entries
- **Property (P)**: The context menu for home-pinned items should include Favorite/Unfavorite, Duplicate, Delete, and multi-launch entries with correct keyboard shortcuts and ordering
- **Preservation**: The full `Build()` context menu, `BuildRepairOnly()`, elevation commands, folder/link commands, status, and edit entries must remain unchanged
- **BuildForHomePin()**: The method in `ShortcutContextCommands.cs` that constructs context menu items for shortcuts displayed on the CmdPal home page
- **Build()**: The full context menu builder used on the fallback/search page that includes all commands
- **Multi-launch entries**: Individual launch command entries (e.g., "dotnet build", "npm start") shown when a shortcut has more than one enabled launch command

## Bug Details

### Bug Condition

The bug manifests when a user right-clicks (or opens the context menu for) a shortcut on the QuickShell home page. The `BuildForHomePin()` method constructs the context menu but only includes elevation, folder/link commands, status, diagnostics, and Edit — omitting Favorite/Unfavorite, Duplicate, Delete, and multi-launch entries that `Build()` provides.

**Formal Specification:**
```
FUNCTION isBugCondition(input)
  INPUT: input of type ContextMenuRequest
  OUTPUT: boolean
  
  RETURN input.source == "HomePin"
         AND input.shortcut.needsRepair == false
         AND (
           input.expectedCommand IN ["Favorite", "Unfavorite", "Duplicate", "Delete"]
           OR (input.shortcut.enabledLaunchCount > 1 AND input.expectedCommand == "IndividualLaunch")
         )
END FUNCTION
```

### Examples

- User right-clicks a pinned shortcut "MyProject" on the home page → context menu shows elevation, explorer, copy path, status, edit — but no Favorite, Duplicate, or Delete. **Expected**: all those commands should appear.
- User right-clicks a pinned shortcut with 3 launch commands (Terminal, VS Code, Browser) → context menu does NOT list the individual launch commands. **Expected**: "Terminal", "VS Code", "Browser" entries should appear at the top.
- User right-clicks the same shortcut on the search/fallback page → full context menu appears including Favorite, Duplicate, Delete. **This already works correctly.**
- User right-clicks a broken shortcut on the home page → `BuildRepairOnly()` is used, which intentionally has a minimal menu. **This is correct and unchanged.**

## Expected Behavior

### Preservation Requirements

**Unchanged Behaviors:**
- The full `Build()` method and its output for the fallback/search page must remain identical
- `BuildRepairOnly()` must remain unchanged — it's for broken shortcuts
- Elevation toggle (Run as Admin / Run Normally) in `BuildForHomePin()` must continue to work
- Folder/link commands (Open in Explorer, Copy Path, Dev Server, Repo, Companion App) must continue to work
- Workspace Status and Edit (Ctrl+E) must continue to work
- Undo/Redo must NOT be added to `BuildForHomePin()` (they are page-level)
- Pinned move commands (move up/down/top/bottom) must NOT be added to `BuildForHomePin()` (they are for QuickShell favorites ordering on a different page)
- The `#if DEBUG` "CmdPal form repros" entry in `QuickShellCommandsProvider.cs` is unaffected

**Scope:**
All inputs that do NOT involve the home-page pinned context menu should be completely unaffected by this fix. This includes:
- Context menus opened from search/fallback page (uses `Build()`)
- Context menus for broken shortcuts (uses `BuildRepairOnly()`)
- Page-level undo/redo commands
- Any other UI interaction outside of the context menu

## Hypothesized Root Cause

Based on code analysis, the root cause is clear and confirmed:

1. **Intentional omission that went too far**: `BuildForHomePin()` was written as a stripped-down version of `Build()` to exclude page-level actions (Undo/Redo) and pinned move commands. However, the developer also omitted Favorite, Duplicate, Delete, and multi-launch entries that should be universal.

2. **No multi-launch section**: `BuildForHomePin()` does not call `ShortcutLaunchNormalization.GetLaunchesForDisplay()` or add individual `OpenShortcutLaunchCommand` entries. The `Build()` method adds these at the top of the menu when `enabledLaunches.Count > 1`.

3. **Missing Favorite command**: `BuildForHomePin()` does not create a `ToggleFavoriteShortcutCommand` or add it with the Ctrl+F shortcut.

4. **Missing Duplicate command**: `BuildForHomePin()` does not create a `DuplicateShortcutCommand` or add it with the Ctrl+Shift+D shortcut.

5. **Missing Delete command**: `BuildForHomePin()` does not create a `DeleteShortcutCommand` or add it with the Ctrl+Delete shortcut.

## Correctness Properties

Property 1: Bug Condition - Home Pin Context Menu Includes All Management Commands

_For any_ shortcut displayed on the home page (CmdPal pinned item) that does not need repair, the fixed `BuildForHomePin()` method SHALL return a context menu array that includes Favorite/Unfavorite (with Ctrl+F shortcut), Duplicate (with Ctrl+Shift+D shortcut), Delete (with Ctrl+Delete shortcut, marked critical), and individual launch entries when the shortcut has more than one enabled launch command.

**Validates: Requirements 2.1, 2.2, 2.3, 2.4**

Property 2: Preservation - Existing Commands and Exclusions Unchanged

_For any_ shortcut displayed on the home page, the fixed `BuildForHomePin()` method SHALL continue to include elevation toggle, folder/link commands, workspace status, launch diagnostics, and Edit (Ctrl+E), while continuing to exclude Undo/Redo and pinned move commands. The `Build()` and `BuildRepairOnly()` methods SHALL produce identical output to the original code.

**Validates: Requirements 2.5, 2.6, 3.1, 3.2, 3.3, 3.4, 3.5**

## Fix Implementation

### Changes Required

**File**: `QuickShell/Services/ShortcutContextCommands.cs`

**Method**: `BuildForHomePin()`

**Specific Changes**:

1. **Add multi-launch entries at the top** (before elevation): After the repair check and before `AddElevationContextCommand`, add the same multi-launch logic that `Build()` uses:
   ```csharp
   var enabledLaunches = ShortcutLaunchNormalization.GetLaunchesForDisplay(shortcut);
   if (enabledLaunches.Count > 1)
   {
       foreach (var launch in enabledLaunches)
       {
           items.Add(new CommandContextItem(new OpenShortcutLaunchCommand(shortcut, launch, settings))
           {
               Title = ShortcutDisplay.GetLaunchContextMenuTitle(launch, enabledLaunches),
               Icon = new IconInfo(TerminalLaunchGlyphs.GetForLaunch(launch)),
           });
       }
   }
   ```

2. **Add Favorite/Unfavorite command** after the Edit entry (same position as in `Build()`):
   ```csharp
   var favoriteCommand = new ToggleFavoriteShortcutCommand(shortcut.Name, onChanged, shortcut.IsPinned);
   items.Add(WithShortcut(
       favoriteCommand,
       ctrl: true, alt: false, shift: false,
       VirtualKey.F,
       title: favoriteCommand.Name,
       showInHoverActions: true,
       hoverOrder: HoverOrderFavorite));
   ```

3. **Add Duplicate command** after Favorite:
   ```csharp
   var duplicateCommand = new DuplicateShortcutCommand(shortcut, onChanged);
   items.Add(WithShortcut(
       duplicateCommand,
       ctrl: true, alt: false, shift: true,
       VirtualKey.D,
       title: duplicateCommand.Name,
       showInHoverActions: true,
       hoverOrder: HoverOrderDuplicate));
   ```

4. **Add Delete command** after Duplicate:
   ```csharp
   var deleteCommand = new DeleteShortcutCommand(shortcut.Name, onChanged);
   items.Add(WithShortcut(
       deleteCommand,
       ctrl: true, alt: false, shift: false,
       VirtualKey.Delete,
       title: deleteCommand.Name,
       isCritical: true,
       showInHoverActions: true,
       hoverOrder: HoverOrderDelete));
   ```

5. **No changes to `Build()` or `BuildRepairOnly()`** — these methods are correct as-is.

## Testing Strategy

### Validation Approach

The testing strategy follows a two-phase approach: first, surface counterexamples that demonstrate the bug on unfixed code, then verify the fix works correctly and preserves existing behavior.

### Exploratory Bug Condition Checking

**Goal**: Surface counterexamples that demonstrate the bug BEFORE implementing the fix. Confirm the root cause that `BuildForHomePin()` simply doesn't add the commands.

**Test Plan**: Write tests that call `BuildForHomePin()` with various shortcut configurations and assert the presence of Favorite, Duplicate, Delete, and multi-launch entries. Run these tests on the UNFIXED code to observe failures.

**Test Cases**:
1. **Single-launch shortcut**: Call `BuildForHomePin()` with a normal shortcut → assert Favorite, Duplicate, Delete are present (will fail on unfixed code)
2. **Multi-launch shortcut**: Call `BuildForHomePin()` with a shortcut that has 3 enabled launches → assert individual launch entries are present (will fail on unfixed code)
3. **Favorite toggle text**: Call `BuildForHomePin()` with `IsPinned=true` → assert the command shows "Unfavorite" text (will fail on unfixed code)
4. **Delete is critical**: Call `BuildForHomePin()` → assert the Delete entry has `IsCritical=true` (will fail on unfixed code)

**Expected Counterexamples**:
- The returned `CommandContextItem[]` from `BuildForHomePin()` does not contain any item with title matching Favorite/Unfavorite, Duplicate, or Delete
- No `OpenShortcutLaunchCommand` items are present regardless of launch count

### Fix Checking

**Goal**: Verify that for all inputs where the bug condition holds, the fixed function produces the expected behavior.

**Pseudocode:**
```
FOR ALL shortcut WHERE isHomePin(shortcut) AND NOT needsRepair(shortcut) DO
  result := BuildForHomePin_fixed(shortcut, onChanged, settings)
  ASSERT containsCommandOfType(result, "ToggleFavoriteShortcutCommand")
  ASSERT containsCommandOfType(result, "DuplicateShortcutCommand")
  ASSERT containsCommandOfType(result, "DeleteShortcutCommand")
  IF enabledLaunchCount(shortcut) > 1 THEN
    ASSERT containsLaunchEntries(result, enabledLaunchCount(shortcut))
  END IF
END FOR
```

### Preservation Checking

**Goal**: Verify that for all inputs where the bug condition does NOT hold, the fixed function produces the same result as the original function.

**Pseudocode:**
```
FOR ALL shortcut WHERE NOT isHomePin(shortcut) DO
  ASSERT Build_original(shortcut) = Build_fixed(shortcut)
END FOR

FOR ALL shortcut WHERE needsRepair(shortcut) DO
  ASSERT BuildRepairOnly_original(shortcut) = BuildRepairOnly_fixed(shortcut)
END FOR

FOR ALL shortcut WHERE isHomePin(shortcut) DO
  result := BuildForHomePin_fixed(shortcut)
  ASSERT NOT containsCommandOfType(result, "UndoShortcutCommand")
  ASSERT NOT containsCommandOfType(result, "RedoShortcutCommand")
  ASSERT NOT containsCommandOfType(result, "MoveFavoriteShortcutCommand")
END FOR
```

**Testing Approach**: Property-based testing is recommended for preservation checking because:
- It generates many shortcut configurations (varying launch counts, IsPinned states, repair states, link URLs) automatically
- It catches edge cases like shortcuts with zero launches, empty names, or unusual states
- It provides strong guarantees that `Build()` and `BuildRepairOnly()` are unmodified

**Test Plan**: Observe behavior on UNFIXED code for `Build()` and `BuildRepairOnly()`, capture the exact command types and ordering, then write property-based tests to verify those outputs remain identical after the fix.

**Test Cases**:
1. **Build() output preservation**: For any shortcut, `Build()` returns the same commands with same ordering, shortcuts, and critical flags as before the fix
2. **BuildRepairOnly() output preservation**: For any broken shortcut, `BuildRepairOnly()` returns the same minimal menu
3. **No Undo/Redo in BuildForHomePin()**: For any shortcut, `BuildForHomePin()` never contains undo/redo commands
4. **No pinned move in BuildForHomePin()**: For any shortcut, `BuildForHomePin()` never contains move commands

### Unit Tests

- Test `BuildForHomePin()` with a single-launch shortcut: verify Favorite, Duplicate, Delete present with correct shortcuts
- Test `BuildForHomePin()` with a multi-launch shortcut (2+ launches): verify individual launch entries appear
- Test `BuildForHomePin()` with `IsPinned=true`: verify Favorite command shows correct toggle text
- Test `BuildForHomePin()` with a shortcut needing repair: verify it still delegates to `BuildRepairOnly()`
- Test that `Build()` output is unchanged for various configurations

### Property-Based Tests

- Generate random `TerminalShortcut` configurations (varying launch counts, IsPinned, DevServerUrl, RepoUrl) and verify `BuildForHomePin()` always includes Favorite, Duplicate, Delete when not needing repair
- Generate random shortcuts with `enabledLaunches.Count > 1` and verify `BuildForHomePin()` includes exactly that many launch entries
- Generate random shortcuts and verify `BuildForHomePin()` never includes Undo, Redo, or Move commands
- Generate random shortcuts and verify `Build()` produces identical output to the original implementation

### Integration Tests

- Open the QuickShell home page, right-click a pinned shortcut, verify full context menu appears with Favorite, Duplicate, Delete
- Open the QuickShell home page with a multi-launch shortcut, right-click, verify individual launch entries appear
- Click Favorite from the home page context menu, verify the shortcut's pinned state toggles
- Click Delete from the home page context menu, verify the shortcut is removed
- Click Duplicate from the home page context menu, verify a copy is created
