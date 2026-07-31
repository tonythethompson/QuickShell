# Command Palette Extension Gallery submission

Ready-to-submit package for [microsoft/CmdPal-Extensions](https://github.com/microsoft/CmdPal-Extensions).

## Store listing

- **Product (Store):** Quick Shell for CmdPal
- **Product (Command Palette):** Quick Shell
- **Store ID:** `9PC8S6LNRT3R`
- **URL:** https://apps.microsoft.com/detail/9PC8S6LNRT3R

Publishing to the Microsoft Store registers the extension with Command Palette, but **does not** add it to the in-app Extension Gallery. That requires a separate PR to `microsoft/CmdPal-Extensions`.

## Submit / update

Listing already lives at `extensions/tonythethompson/quickshell/` upstream. To refresh logo, screenshots, or metadata:

1. Keep this folder as the source of truth (`icon.png`, `extension.json`, `screenshots/`)
2. Run `scripts/submit-cmdpal-gallery.ps1` if `gh` is authenticated (syncs your fork, pushes an update branch, opens a PR)
3. After merge, maintainers regenerate `extensions.json`; Command Palette picks up the gallery change

Manual path: fork https://github.com/microsoft/CmdPal-Extensions, replace `extensions/tonythethompson/quickshell/`, open a PR titled `Update tonythethompson.quickshell gallery listing`.

## Gallery title note

The gallery entry uses **Quick Shell** in Command Palette. The Store listing title **Quick Shell for CmdPal** is set in Partner Center, not in the MSIX manifest (CmdPal reads package display name from the manifest).
