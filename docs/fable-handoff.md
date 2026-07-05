# QuickShell Raycast handoff for Fable

## Goal

Build **QuickShell Raycast** as a **Raycast-native workspace launcher for developers**.

The MVP should let users:

* save project folders as workspaces
* search and open them quickly from Raycast
* create and edit workspaces without hand-editing JSON
* run launch commands from a workspace
* keep favorites and recents visible
* preserve honest validation and failure states

This is **not** a generic folder opener and **not** a terminal wrapper.

It should feel like a **workspace/session manager**.

---

## Source of truth

Use the GitHub project pages as the task source of truth.

* **Project 3** = MVP execution board
    * `https://github.com/users/tonythethompson/projects/3`
* **Project 4** = full port backlog
    * `https://github.com/users/tonythethompson/projects/4`

### Important rule

Do **not** worry about anything outside the MVP unless you finish the MVP early and still have time.

Project 4 is there as a superset backlog for future parity work.  
It is **not** a blocker for the MVP.

---

## Repo context

The current QuickShell repo is Windows-first and already has a meaningful C# implementation.

The Raycast port should be added **inside the same repo**, but as a separate client.

### Runtime split

* **Raycast extension:** TypeScript + React
* **Current QuickShell app:** C# / Windows / PowerToys
* **Shared contract:** versioned JSON schema

### Architecture rule

Do **not** rewrite the Windows app.  
Do **not** try to make the MVP cross-platform.  
Do **not** broaden the scope because a backlog item looks interesting.

---

## Product framing

QuickShell Raycast is a **keyboard-first, list-first, action-first** dev workflow tool.

It should support:

* search-first workspace access
* predictable workspace editing
* fast launching
* clear errors
* visible recents and favorites
* honest persistence

If it cannot honestly do something, it should show that clearly.

---

## Current project split

### Project 3 — MVP only

Project 3 should stay focused on the first release path.

#### Current MVP issues in Project 3

* **#19** Add `QuickShell.Raycast` extension project
* **#20** Add versioned workspace and launch schema
* **#21** Add workspace and settings storage
* **#22** Implement `Open Workspace` command
* **#23** Implement `Create Workspace` command
* **#24** Implement `Edit Workspace` command
* **#25** Add workspace launch execution
* **#28** Implement `Settings` command
* **#29** Add pinning and recent workspace tracking
* **#30** Improve workspace failure feedback
* **#31** Add core tests for Raycast QuickShell logic

#### Current Project 3 state

At handoff time:

* **#19–21** are **In Progress**
* the rest are **Backlog**

#### Project 3 scope rule

Do not add backlog/parity features into Project 3 unless they are truly necessary for MVP completion.

---

### Project 4 — full port backlog

Project 4 is the superset backlog for everything we may want to port later from the current QuickShell app.

This includes the MVP issue family plus parity and polish work.

#### Project 4 issues

* **#19** Add `QuickShell.Raycast` extension project
* **#20** Add versioned workspace and launch schema
* **#21** Add workspace and settings storage
* **#22** Implement `Open Workspace` command
* **#23** Implement `Create Workspace` command
* **#24** Implement `Edit Workspace` command
* **#25** Add workspace launch execution
* **#28** Implement `Settings` command
* **#29** Add pinning and recent workspace tracking
* **#30** Improve workspace failure feedback
* **#31** Add core tests for Raycast QuickShell logic
* **#32** Add optional workspace launch actions for dev server, repo, and companion app
* **#33** Add resilient storage recovery and backup restore
* **#34** Add undo/redo for workspace edits
* **#35** Add section headers in the workspace list
* **#36** Add copy path action for workspace items
* **#37** Add reset all workspaces with backup restore
* **#38** Add refresh terminal profile list
* **#39** Add favorite workspace reordering
* **#40** Add dirty branch launch safety gate
* **#41** Add dev-script autofill for launch commands
* **#42** Add task type dropdown and scope-based command suggestions
* **#43** Add repo-aware workspace setup suggestions
* **#44** Add companion app presets and detection-based setup

---

## What the MVP must do

The MVP should support:

* search workspaces by:
    * name
    * abbreviation
    * directory
    * launch labels
    * command text
* open a workspace from Raycast
* create a workspace
* edit a workspace
* launch a workspace
* launch a single launch entry
* show favorites and recents
* configure settings
* keep failure states visible and truthful
* keep a versioned schema
* keep core tests around schema / storage / search behavior

---

## MVP behavior expectations

### Open Workspace
The main command should:

* rank exact abbreviation matches first
* surface favorites above non-favorites
* surface recents above older ones
* expose actions for:
    * open
    * edit
    * favorite / unfavorite
    * duplicate
    * delete
    * open folder

### Create / Edit Workspace
The workspace forms should support:

* workspace name
* directory
* abbreviation
* favorite toggle
* terminal target
* optional profile
* optional startup command
* launch entry editing

### Launch execution
The launch engine should support:

* Windows Terminal
* PowerShell
* pwsh
* cmd
* WSL
* running as administrator
* a single launch entry
* all enabled launch entries
* clear errors for missing folders or invalid targets

### Settings
The settings surface should include:

* default terminal behavior
* a toggleable Recent workspaces section
* the recent list showing **8** items when enabled
* settings persistence

### Failure behavior
If something is invalid:

* show a clear error
* do not pretend the launch or save succeeded
* do not silently swallow failures

---

## MVP cuts already made

The following were intentionally pruned from Project 3 and should remain backlog-only unless the MVP ends early:

* Discover Git Repos
* workspace import/export

These are useful, but they are not required for the first release.

---

## Repo behaviors that matter later

The current QuickShell repo contains a lot of useful behavior that should be preserved eventually, but it should not block the MVP.

These are already represented in Project 4:

* optional dev-server / repo / companion-app launch actions
* resilient storage recovery and `.bak` restore
* undo / redo
* section headers
* copy path
* reset all workspaces
* refresh terminal profile list
* favorite reordering
* dirty branch launch safety gate
* dev-script autofill
* task type dropdown / scope-driven command suggestions
* repo-aware workspace setup suggestions
* companion app presets and detection-based setup

---

## Implementation guidance

### Use the repo as the evidence source
When implementing, inspect the current QuickShell code and use that behavior as the truth.

Do not invent behavior if the repo already has a real flow.

### Keep the Raycast MVP narrow
Focus on:
* schema
* storage
* create/edit/open
* launch execution
* settings
* recents/favorites
* failure feedback
* tests

### Avoid scope creep
Do not spend MVP time on:
* discovery
* import/export
* undo/redo
* reset-all
* companion-app presets
* task type logic
* repo-aware setup suggestions
* launch autofill
* branch safety gating

Those belong in Project 4 unless the MVP is already done.

---

## Suggested implementation order

### Phase 1
* #19 scaffold the Raycast extension
* #20 define the schema
* #21 implement storage

### Phase 2
* #22 Open Workspace
* #23 Create Workspace
* #24 Edit Workspace
* #25 Launch execution

### Phase 3
* #28 Settings
* #29 pinning / recents
* #30 failure feedback
* #31 tests

### Phase 4
If the MVP is done and there is still time:
* pull from Project 4

---

## Code quality workflow for QuickShell

Use this workflow while working:

1. Start from the issue
2. Read the relevant source files
3. Implement one vertical slice
4. Add tests at the same time
5. Smoke the Windows behavior when launch/storage is involved
6. Update the project item stage
7. Only then move on

### Quality gates
QuickShell changes are not done unless they have:

* code
* tests
* project tracking
* smoke verification where needed

### Extra rule for Raycast work
The Raycast UI should stay deterministic and simple.  
Do not rely on hidden app state or broad refactors to make it work.

---

## What to ignore

Ignore the unrelated untitled projects on the account.

They are not part of this handoff.

---

## Hand-off summary

### Project 3
This is the MVP board.

It should stay lean and execution-focused.

### Project 4
This is the broader port backlog.

It is only for later, after the MVP is in good shape.

### Working rule
Use Project 3 first.  
Use Project 4 only if time remains after the MVP is stable.

---

## Final instruction to Fable

Do not treat the backlog as part of the first release.

Build the MVP from **Project 3**, keep it tight, and only touch **Project 4** if you finish early.