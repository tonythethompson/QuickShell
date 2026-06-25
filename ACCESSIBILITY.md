# Accessibility testing for Quick Shell

Quick Shell is a **PowerToys Command Palette extension**. Most UI (search box, list chrome, keyboard routing) is rendered by Command Palette. This checklist covers what **you** ship: list item text, context actions, Adaptive Card forms, and the folder picker.

Only check **"This product has been tested to meet accessibility guidelines"** in Partner Center if you have verified the scenarios below. See [Accessibility in the Store](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessibility-in-the-store) and [Product declarations](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/product-declarations).

## Quick start

1. Install the build you plan to ship (MSIX recommended):

   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts/deploy.ps1
   ```

2. In PowerToys Command Palette, run **Reload Command Palette Extension**.

3. Open Windows accessibility tools:

   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts/open-a11y-settings.ps1
   ```

## Primary scenarios to test

Test each flow with **keyboard only**, then again with **Narrator** on.

| # | Scenario | Pass criteria |
|---|----------|---------------|
| 1 | Search and open a shortcut | Arrow keys select a row; Enter launches the terminal |
| 2 | Create a new shortcut | Enter on **Create new shortcut**; Tab through all fields; Save works |
| 3 | Edit / pin / duplicate / delete | **Ctrl+K** opens More actions; each action completes |
| 4 | Open as administrator | Available in More actions when not always-admin |
| 5 | Browse for folder | Browse button activatable; folder picker usable with keyboard |
| 6 | Reload shortcuts | **Refresh terminals** runs without error |

## Keyboard navigation (Command Palette)

| Key | Action |
|-----|--------|
| `Win+Alt+Space` | Open Command Palette (default; may differ in your PowerToys settings) |
| Arrow keys | Move selection in the results list |
| `Enter` | Run the selected command |
| `Ctrl+Enter` | Open as administrator (when available) |
| `Ctrl+K` | **More** menu (Edit, Pin, Delete, etc.) |
| `Esc` | Go back / close |

**Note:** `Shift+F10` opens the context menu for the **focused** control. If focus is in the search box, you will see Paste/Undo — not shortcut actions. Use arrow keys to select a list item, then `Ctrl+K` for extension actions.

## Narrator

Turn on Narrator (`Win+Ctrl+Enter`) and verify:

- [ ] Shortcut **Title** is announced clearly (the shortcut name)
- [ ] **Subtitle** adds useful context without being unreadable noise
- [ ] Form fields announce their **labels** (Name, Search keyword, Folder path, Command, Terminal)
- [ ] **Browse folder** and **Save shortcut** are identifiable by name
- [ ] Save/delete/pin actions give understandable feedback (toast or list update)

## Visual accessibility

- [ ] Test at **125%** and **150%** display scale — form text and list rows are not clipped
- [ ] Test at least one **contrast theme** (Aquatic or Desert) — list and form remain readable
- [ ] Information is not conveyed by **color alone** (e.g. **Admin** and **Pinned** tags have text, not only icon/color)
- [ ] Body text meets roughly **4.5:1** contrast where you control styling (Adaptive Card defaults are usually fine; avoid custom low-contrast HTML in cards)

## Automated tools (limited scope)

| Tool | What to run it on |
|------|-------------------|
| [Accessibility Insights for Windows](https://accessibilityinsights.io/docs/windows/overview/) | Folder browse dialog (WinForms) |
| [Inspect](https://learn.microsoft.com/en-us/windows/win32/winauto/inspect-objects) | Folder picker UIA tree |
| UIAVerify | Same — resolve Priority 1 issues if reported |

Command Palette list UI runs inside PowerToys; you cannot attach Insights to it from the Quick Shell process. Rely on keyboard + Narrator for CmdPal chrome.

## Extension content checklist

- [ ] Every shortcut has a descriptive **Name** (not only an abbreviation)
- [ ] Adaptive Card inputs have **labels** and **errorMessage** on required fields
- [ ] Folder picker can be completed without a mouse
- [ ] More actions reachable via **Ctrl+K** on a selected shortcut
- [ ] Primary scenarios tested with Narrator
- [ ] Primary scenarios tested at increased DPI and one high-contrast theme

## Store submission notes

In **Notes for certification**, include:

1. Requires **Microsoft PowerToys** with **Command Palette** enabled.
2. Install Quick Shell, run **Reload Command Palette Extension**, search **Quick Shell**.
3. Test create shortcut, open shortcut, and **Ctrl+K** More actions.

Declare accessibility only for **your extension content** (names, forms, picker). You are not certifying all of Command Palette — that shell is owned by PowerToys.

## References

- [Develop accessible Windows apps](https://learn.microsoft.com/en-us/windows/apps/develop/accessibility)
- [Accessibility testing](https://learn.microsoft.com/en-us/windows/apps/develop/accessibility/accessibility-testing)
- [Accessibility checklist (Microsoft)](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessibility-checklist)
