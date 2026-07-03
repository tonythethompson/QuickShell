---
layout: page
title: Get started
description: Create workspaces, use home keywords, and manage Quick Shell from Command Palette.
---

# Get started

Let's create your first Quick Shell workspace and learn the basics.

<div class="callout">
  <strong>Don't have Quick Shell yet?</strong> Install from the
  <a href="https://apps.microsoft.com/detail/9PC8S6LNRT3R" target="_blank" rel="noopener">Microsoft Store</a>
  (Store ID: <code>9PC8S6LNRT3R</code>) or see the full
  <a href="{{ '/install/' | relative_url }}">install guide</a> for WinGet and GitHub downloads.
</div>

## Launch Quick Shell

Press **Win + Alt + Space** to open PowerToys Command Palette, search for **Quick Shell**, and press **Enter**.

## Create your first workspace

1. In Quick Shell, select **Create workspace** (or press **Ctrl+N**)
2. Pick a **folder** — use **Browse folder** or **Paste path**. The **name** fills in from the folder name (e.g., `MyApp` from `C:\Projects\MyApp`); change it if you want something else
3. (Optional) Set:
   - One or more **commands** and **terminal profiles** (add extra launches for API + frontend, shell + dev server, and so on)
   - A **dev server URL** or **companion app** to open when the workspace runs
   - Run as administrator (if needed)
4. **Save workspace**

That's it! Next time you open Command Palette, search for the workspace name and press **Enter** to jump there.

## Discover git repos

On the Quick Shell home list, choose **Discover git repos** to scan folders on your PC and add repositories as workspaces without typing paths manually.

## Your home list

When you open Quick Shell, the top rows are:

1. **Create workspace** (**Ctrl+N**)
2. **Discover git repos**
3. **Quick Shell settings**

Below that, workspaces appear under **Favorites**, **Recent**, and **Workspaces** (favorites and recents are not repeated in the main list).

## Keyboard shortcuts

Use these shortcuts inside Quick Shell to work faster:

| Action | Keyboard |
|--------|----------|
| Create new workspace | **Ctrl+N** |
| Edit selected workspace | **Ctrl+E** |
| Favorite/unfavorite | **Ctrl+F** |
| Duplicate workspace | **Ctrl+Shift+D** |
| Delete workspace | **Ctrl+Delete** |
| Move favorite up/down | **Ctrl+Alt+Up** / **Ctrl+Alt+Down** |
| Open context menu | **Ctrl+K** or click **⋯** |
| Run as administrator | **Ctrl+Enter** |
| Undo / Redo | **Ctrl+Z** / **Ctrl+Y** |
| Open settings | Select the **Quick Shell settings** list row, or use **⋯** menu |

## Quick Shell settings

Select **Quick Shell settings** on the home list to:

- Set your **default terminal** and terminal **profile**
- Choose how many **recent workspaces** appear on the home page
- **Export workspaces** (backup to a file)
- **Import workspaces** (restore from a backup or another PC)
  - Choose **Merge** to add new ones to your existing list
  - Choose **Replace all** to overwrite everything
- **Reset all workspaces** (a `.bak` backup from your last save is kept)
- **Refresh terminal list** after installing a new terminal app

## Home keywords

Make your most-used workspaces even faster by setting a **home keyword**:

1. Edit a workspace
2. Set a **home keyword** (e.g., `api`, `web`, `docs`)
3. Now press **Win + Alt + Space**, then type your keyword on the home screen to jump there directly — no searching needed

## Where your data is stored

Quick Shell saves everything locally on your PC (no cloud sync):

- **Workspaces:** `%LOCALAPPDATA%\QuickShell\shortcuts.json`
- **Settings:** `%LOCALAPPDATA%\QuickShell\settings.json`

You can back these up or move them to another PC using **Export workspaces** / **Import workspaces**.

## Tips and tricks

- **Multiple launches:** Add more than one terminal or command to a single workspace instead of duplicating the folder
- **Dev server URL:** Save a local URL (e.g. `http://localhost:5173`) and open it in your browser when the workspace runs
- **Companion app:** Launch VS Code, Obsidian, or another app with the workspace folder when you open a project
- **WSL support:** Save WSL project paths and open them in Windows Terminal's WSL profile
- **Admin mode:** Use **Ctrl+Enter** or check **Launch elevated** for elevated commands

## Need help?

- **Install:** [Microsoft Store](https://apps.microsoft.com/detail/9PC8S6LNRT3R){:target="_blank"} or the [Install]({{ '/install/' | relative_url }}) guide
- **Troubleshooting:** See the [Support]({{ '/support/' | relative_url }}) page
- **Source code:** View the full [GitHub repository](https://github.com/tonythethompson/QuickShell){:target="_blank"}
- **Questions:** Email [{{ site.author.email }}](mailto:{{ site.author.email }})
