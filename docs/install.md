---
layout: page
title: Install
description: Install Quick Shell from the Microsoft Store, WinGet, or GitHub Releases.
---

# Install Quick Shell

Pick the launcher you use. **Command Palette** and **PowerToys Run** share the same Windows installers and `%LocalAppData%\QuickShell\` data.
**Raycast** installs from the Raycast Store and keeps its own storage (import/export JSON to bridge).

## Choose your installation method

### Microsoft Store

The easiest way to install Command Palette and stay updated:

1. Open [**Quick Shell for CmdPal in the Microsoft Store**](https://apps.microsoft.com/detail/9PC8S6LNRT3R){:target="_blank"} (Store ID: `9PC8S6LNRT3R`)
2. Click **Install**

Or search **Quick Shell for CmdPal** in the Store app (listing title). After install, open Command Palette and search **Quick Shell**.

<div class="callout">
  <strong>Command Palette only:</strong> the Store package does not include PowerToys Run.
  Add Run from a GitHub ZIP or use the bundled WinGet / GitHub EXE (below).
</div>

<div class="callout">
  <strong>Command Palette extensions in the Store:</strong> You can also browse related extensions with the
  <a href="ms-windows-store://assoc/?Tags=AppExtension-com.microsoft.commandpalette">Command Palette extension tag</a>
  in the Microsoft Store (opens the Store app on Windows).
</div>

### WinGet (Command Line)
{: #winget-command-line}

Both Command Palette packages are live on WinGet. Prefer the bundled ID if you also want PowerToys Run (`qs`).

```powershell
# Command Palette + PowerToys Run (recommended for most users)
winget install tonythethompson.QuickShell

# Command Palette only (same scope as Microsoft Store)
winget install tonythethompson.QuickShellforCmdPal

# PowerToys Run only (when that package is listed)
winget install tonythethompson.QuickShellforRun
```

Restart PowerToys after the bundled or Run-only install so Run picks up the plugin.

### GitHub Releases

Download an installer directly:

1. Go to [GitHub Releases](https://github.com/tonythethompson/QuickShell/releases){:target="_blank"}
2. Pick the installer for your PC:
   - **Bundled:** `QuickShell-Setup-*-x64.exe` or `*-arm64.exe` (CmdPal + Run)
   - **CmdPal only:** `QuickShellforCmdPal-Setup-*-x64.exe` or `*-arm64.exe`
   - **Run only:** `QuickShellforRun-Setup-*-x64.exe` / `*-arm64.exe`, or `QuickShell.Run-*.zip`
3. Run the installer

Choose **x64** for most PCs, **ARM64** only if you're on an ARM-based Windows device.

### Raycast Store

1. Install [Raycast](https://www.raycast.com/){:target="_blank"} (Windows or macOS)
2. Install **Quick Shell** from the [Raycast Store](https://www.raycast.com/store){:target="_blank"}
3. Search `qs`, `quickshell`, or a workspace home keyword in Raycast

Raycast does not share `%LOCALAPPDATA%\QuickShell\shortcuts.json` with CmdPal/Run. Use Import/Export JSON to move workspaces between hosts.

## Complete setup (PowerToys)

After a PowerToys install, follow these steps:

1. **Restart PowerToys** (WinGet / GitHub EXE installs the Run plugin; Store installs CmdPal only)
2. Open **PowerToys Command Palette** (press **Win + Alt + Space**)
3. Search for **Reload Command Palette Extension** and run it
4. Search for **Quick Shell** — you should see it in the results

Optional: open **PowerToys Run** (**Alt+Space**), type **`qs`**, to use the same shortcuts from Run.

<div class="callout">
  <strong>Not showing up?</strong> Make sure Command Palette is enabled in PowerToys settings (Settings → Command Palette → enabled). 
  Then run <strong>Reload Command Palette Extension</strong> again. If it still doesn't appear, restart PowerToys.
</div>

## Next steps

- **[Get started]({{ '/getting-started/' | relative_url }})** — Create your first shortcut
- **[Support]({{ '/support/' | relative_url }})** — Troubleshooting and help
