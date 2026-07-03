---
layout: page
title: Support
description: Contact, bug reports, and feature requests for Quick Shell.
---

# Support

## Common issues

### Extension doesn't appear after installation

1. Open **PowerToys Command Palette** (press **Win + Alt + Space**)
2. Search for and run **Reload Command Palette Extension**
3. Confirm Command Palette is enabled in PowerToys settings (Settings → Command Palette → toggle on)
4. Try again, and restart PowerToys if needed

### Shortcuts disappeared after an update

A backup is automatically created:

1. Open File Explorer and paste this path: `%LOCALAPPDATA%\QuickShell\`
2. Look for `shortcuts.json.bak` — this is your backup
3. Contact us for help restoring it

### Terminal list is empty or outdated

In Quick Shell, open **settings** and click **Refresh terminal list**. This detects newly installed terminals.

### Duplicate Quick Shell in Windows Settings → Apps

You have multiple installations (Store + WinGet, or multiple local versions). Uninstall all but one:

1. Open Settings → Apps → Installed apps
2. Search for "Quick Shell" or "Quick Shell for CmdPal"
3. Click the **⋮** menu and select **Uninstall**
4. Keep only one installation method (recommend: Microsoft Store)

### Command not running when folder opens

- Check that the **command** field is filled correctly
- Make sure the command works when you type it manually in the terminal
- Some commands need the terminal to wait — test with `pause` or `Read-Host` at the end
- For scripts, use the full path: `C:\scripts\build.bat` instead of just `build.bat`

## Report a bug or request a feature

### GitHub Issues (fastest response)

1. Visit [github.com/tonythethompson/QuickShell/issues](https://github.com/tonythethompson/QuickShell/issues){:target="_blank"}
2. Check existing issues first to avoid duplicates
3. Click **New issue**

**For a bug report, include:**
- **What you expected:** What should happen?
- **What happened:** What actually occurred?
- **How to reproduce:** Step-by-step instructions
- **Your system:**
  - Windows version (e.g., Windows 11 23H2)
  - PowerToys version (from PowerToys settings)
  - Quick Shell version (check in App Settings → Apps)
  - How you installed it (Microsoft Store, WinGet, or GitHub release)
- **Error messages:** Copy any error text or screenshots

**For a feature request:**
- Describe the problem you're trying to solve
- Explain how the feature would help you
- Tell us if you'd be willing to test a preview version

### Email support

For private questions not suited for GitHub:

**[{{ site.author.email }}](mailto:{{ site.author.email }})**

Please include:
- Windows version
- PowerToys version
- Quick Shell version
- What you're experiencing

## Privacy

Quick Shell stores **zero data in the cloud**. All shortcuts and settings stay on your PC.

See the full [Privacy policy]({{ '/privacy/' | relative_url }}) for details on what Quick Shell collects (spoiler: nothing).

Have privacy questions? Email [{{ site.author.email }}](mailto:{{ site.author.email }}).
