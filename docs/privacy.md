---
layout: page
title: Privacy
description: Quick Shell privacy policy — local data only, no analytics, no account.
---

# Privacy policy

**Last updated:** June 26, 2026  
**Publisher:** Tony Thompson  
**Product:** Quick Shell — a PowerToys Command Palette extension

## Summary

Quick Shell stores your shortcut settings **on your PC only**. The extension does **not** send your data to the developer, does not use analytics, and does not require an account.

## What data the app uses

Quick Shell reads and writes data you provide when using the extension, including:

- Shortcut **names**
- Folder **paths** you choose (paths may include your Windows user name)
- Optional **home keywords**, **terminal** choices, **commands**, and **favorites**

This information is saved in files under:

`%LocalAppData%\QuickShell\`

For example: `shortcuts.json`, optional edit-draft files, and extension settings managed through Command Palette.

## What the app does not do

Quick Shell does **not**:

- Transmit shortcut data or usage to the developer or third-party servers
- Sell or share your information
- Run advertising or tracking
- Require sign-in

The extension runs locally as part of **PowerToys Command Palette**. It launches terminals and folder pickers you choose; it does not browse the web or upload your shortcuts.

## Permissions

The Store package declares **runFullTrust** so Quick Shell can register with Command Palette, open terminals, and use standard Windows dialogs (for example, folder browse). That capability does not change the fact that shortcut data stays on your device.

## Data retention and deletion

Data remains on your PC until you delete it. You can:

- Remove shortcuts inside Quick Shell
- Delete `%LocalAppData%\QuickShell\`
- Uninstall the extension or app package

## Children's privacy

Quick Shell is a general-purpose Windows productivity tool. It is not designed for or directed at children under 13, and the developer does not knowingly collect personal information from children.

## Changes

This policy may be updated when the product changes. The current version is always published on this site and in the [Quick Shell repository](https://github.com/tonythethompson/QuickShell).

## Contact

Questions or privacy requests:

- Email: [{{ site.author.email }}](mailto:{{ site.author.email }})
- GitHub: [Open an issue](https://github.com/tonythethompson/QuickShell/issues)
