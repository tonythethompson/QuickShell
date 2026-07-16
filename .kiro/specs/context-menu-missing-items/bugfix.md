# Bugfix Requirements Document

## Introduction

The context menu for workspace shortcuts on the QuickShell home page (CmdPal pinned items) is missing essential management actions. The `BuildForHomePin()` method in `ShortcutContextCommands.cs` omits Favorite, Duplicate, Delete, and multi-launch entries that are present in the full `Build()` method used on the fallback/search page. This makes it impossible for users to manage their pinned shortcuts without first searching for them.

## Bug Analysis

### Current Behavior (Defect)

1.1 WHEN a shortcut is displayed on the QuickShell home page (pinned item) THEN the system does not show Favorite/Unfavorite in the context menu

1.2 WHEN a shortcut is displayed on the QuickShell home page (pinned item) THEN the system does not show Duplicate in the context menu

1.3 WHEN a shortcut is displayed on the QuickShell home page (pinned item) THEN the system does not show Delete in the context menu

1.4 WHEN a shortcut with multiple launch commands is displayed on the QuickShell home page THEN the system does not show individual launch entries (e.g. "dotnet build") in the context menu

### Expected Behavior (Correct)

2.1 WHEN a shortcut is displayed on the QuickShell home page (pinned item) THEN the system SHALL show Favorite/Unfavorite (Ctrl+F) in the context menu

2.2 WHEN a shortcut is displayed on the QuickShell home page (pinned item) THEN the system SHALL show Duplicate (Ctrl+Shift+D) in the context menu

2.3 WHEN a shortcut is displayed on the QuickShell home page (pinned item) THEN the system SHALL show Delete (Ctrl+Delete) in the context menu

2.4 WHEN a shortcut with multiple launch commands is displayed on the QuickShell home page THEN the system SHALL show individual launch entries for each enabled launch command in the context menu

2.5 WHEN a shortcut is displayed on the QuickShell home page (pinned item) THEN the system SHALL NOT show Undo/Redo commands in the context menu (those belong to the QuickShell page-level, not per-item)

2.6 WHEN a shortcut is displayed on the QuickShell home page (pinned item) THEN the system SHALL NOT show pinned move commands (move up/down/top/bottom) in the context menu (those are for QuickShell favorites ordering on a different page)

### Unchanged Behavior (Regression Prevention)

3.1 WHEN a shortcut is displayed on the fallback/search page THEN the system SHALL CONTINUE TO show the full context menu including elevation, folder/link, status, edit, favorite, duplicate, delete, and pinned move commands

3.2 WHEN a shortcut needs repair THEN the system SHALL CONTINUE TO show the repair-only context menu via `BuildRepairOnly()`

3.3 WHEN a shortcut is displayed on the QuickShell home page THEN the system SHALL CONTINUE TO show Run as Admin / Run Normally (elevation toggle) in the context menu

3.4 WHEN a shortcut is displayed on the QuickShell home page THEN the system SHALL CONTINUE TO show Open in File Explorer, Copy path, and link commands in the context menu

3.5 WHEN a shortcut is displayed on the QuickShell home page THEN the system SHALL CONTINUE TO show Workspace status and Edit (Ctrl+E) in the context menu

3.6 WHEN the application is built in DEBUG configuration THEN the "CmdPal form repros" entry SHALL CONTINUE TO appear only in the debug build context menu (already correctly guarded by `#if DEBUG`)
