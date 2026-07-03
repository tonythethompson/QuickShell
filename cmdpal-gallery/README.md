# Command Palette Extension Gallery submission

Ready-to-submit package for [microsoft/CmdPal-Extensions](https://github.com/microsoft/CmdPal-Extensions).

## Store listing

- **Product (Store):** Quick Shell for CmdPal
- **Product (Command Palette):** Quick Shell
- **Store ID:** `9PC8S6LNRT3R`
- **URL:** https://apps.microsoft.com/detail/9PC8S6LNRT3R

Publishing to the Microsoft Store registers the extension with Command Palette, but **does not** add it to the in-app Extension Gallery. That requires a separate PR to `microsoft/CmdPal-Extensions`.

## Submit

1. Fork https://github.com/microsoft/CmdPal-Extensions
2. Copy `extensions/tonythethompson/quickshell/` from this folder into your fork
3. Open a PR titled `Add tonythethompson.quickshell to gallery`
4. After merge, maintainers regenerate `extensions.json`; the extension then appears under **Command Palette → Settings → Extensions → Gallery**

Or run `scripts/submit-cmdpal-gallery.ps1` if `gh` is authenticated.

## Gallery title note

The gallery entry uses **Quick Shell** in Command Palette. The Store listing title **Quick Shell for CmdPal** is set in Partner Center, not in the MSIX manifest (CmdPal reads package display name from the manifest).
