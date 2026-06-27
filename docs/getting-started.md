---
layout: default
title: Get started
description: Create shortcuts, use home keywords, and manage Quick Shell from Command Palette.
---

# Get started

Let's create your first Quick Shell shortcut and learn the basics.

## Launch Quick Shell

Press **Win + Alt + Space** to open PowerToys Command Palette, search for **Quick Shell**, and press **Enter**.

## Create your first shortcut

1. In Quick Shell, select **Create shortcut** (or press **Ctrl+N**)
2. Enter a **name** (e.g., "My Project") and the **folder path** you want to save
3. (Optional) Set:
   - A **command** to run when opening (e.g., `npm start`)
   - Your preferred **terminal profile** (Windows Terminal, PowerShell, WSL, etc.)
   - Run as administrator (if needed)
4. Save

That's it! Next time you open Command Palette, search "My Project" and press **Enter** to jump there.

## Keyboard shortcuts

Use these shortcuts inside Quick Shell to work faster:

| Action | Keyboard |
|--------|----------|
| Create new shortcut | **Ctrl+N** |
| Edit selected shortcut | **Ctrl+E** |
| Favorite/unfavorite | **Ctrl+F** |
| Open context menu | **Ctrl+K** or click **⋯** |
| Run as administrator | **Ctrl+Enter** |
| Undo / Redo | **Ctrl+Z** / **Ctrl+Y** |
| Open settings | Click the gear icon or use **⋯** menu |

## Quick Shell settings

Click the gear icon or find "Quick Shell settings" in the list to:

- Set your **default terminal** and terminal **profile**
- **Backup shortcuts** (export them to a file)
- **Restore shortcuts** (import from a backup or another PC)
  - Choose **Merge** to add new ones to your existing list
  - Choose **Replace all** to overwrite everything
- **Refresh terminal list** after installing a new terminal app

## Home keywords

Make your most-used shortcuts even faster by setting a **home keyword**:

1. Edit a shortcut
2. Set a **home keyword** (e.g., `api`, `web`, `docs`)
3. Now press **Win + Alt + Space**, then type your keyword on the home screen to jump there directly — no searching needed

## Where your data is stored

Quick Shell saves everything locally on your PC (no cloud sync):

- **Shortcuts:** `%LOCALAPPDATA%\QuickShell\shortcuts.json`
- **Settings:** `%LOCALAPPDATA%\QuickShell\settings.json`

You can back these up or move them to another PC using the Export/Import feature.

## Tips and tricks

- **Run startup commands:** Set a command like `npm start` or `docker-compose up` to run automatically when the folder opens
- **Multiple profiles:** Create multiple shortcuts for the same folder with different terminals or commands
- **WSL support:** Save WSL project paths and open them in Windows Terminal's WSL profile
- **Admin mode:** Use **Ctrl+Enter** or check "Run as administrator" for elevated commands

## Need help?

- **Troubleshooting:** See the [Support]({{ '/support/' | relative_url }}) page
- **Source code:** View the full [GitHub repository](https://github.com/tonythethompson/QuickShell){:target="_blank"}
- **Questions:** Email [{{ site.author.email }}](mailto:{{ site.author.email }})
