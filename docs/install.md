---
layout: page
title: Install
description: Install Quick Shell from the Microsoft Store, WinGet, or GitHub Releases.
---

# Install Quick Shell

Before installing, ensure you have **PowerToys** with **Command Palette** enabled.  
[Install PowerToys](https://learn.microsoft.com/windows/powertoys/install){:target="_blank"} if you haven't already.

## Choose your installation method

### Microsoft Store

The easiest way to install and stay updated:

1. Open [**Quick Shell for CmdPal in the Microsoft Store**](https://apps.microsoft.com/detail/9PC8S6LNRT3R){:target="_blank"} (Store ID: `9PC8S6LNRT3R`)
2. Click **Install**

Or search **Quick Shell for CmdPal** in the Store app (listing title). After install, open Command Palette and search **Quick Shell**.

<div class="callout">
  <strong>Command Palette extensions in the Store:</strong> You can also browse related extensions with the
  <a href="ms-windows-store://assoc/?Tags=AppExtension-com.microsoft.commandpalette">Command Palette extension tag</a>
  in the Microsoft Store (opens the Store app on Windows).
</div>

### WinGet (Command Line)

Install from PowerShell or Command Prompt:

```powershell
winget install tonythethompson.QuickShell
```

This installer registers the **Command Palette** extension and installs the **PowerToys Run** plugin (`qs` in Alt+Space). Restart PowerToys after install so Run picks up the plugin.

### GitHub Releases

Download the installer directly:

1. Go to [GitHub Releases](https://github.com/tonythethompson/QuickShell/releases){:target="_blank"}
2. Download the latest **x64** or **ARM64** **EXE** installer (`QuickShell-Setup-*-x64.exe` or `*-arm64.exe`)
3. Run the installer

Same as WinGet: includes both Command Palette and PowerToys Run. Restart PowerToys after install.

Choose **x64** for most PCs, **ARM64** only if you're on an ARM-based Windows device.

Standalone Run-only ZIPs (`QuickShell.Run-*.zip`) are also on Releases if you already use the Store build and only want the Run plugin.

## Complete setup

After installation, follow these steps:

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
