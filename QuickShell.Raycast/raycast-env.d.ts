/// <reference types="@raycast/api">

/* 🚧 🚧 🚧
 * This file is auto-generated from the extension's manifest.
 * Do not modify manually. Instead, update the `package.json` file.
 * 🚧 🚧 🚧 */

/* eslint-disable @typescript-eslint/ban-types */

type ExtensionPreferences = {
  /** Default Terminal App - Terminal application used when a workspace launch uses the Quick Shell default. On macOS use Terminal or iTerm2; Windows values are ignored on Mac. */
  "terminalApplication": "system" | "wt" | "conhost" | "it" | "terminal" | "iterm",
  /** Default Profile - Profile name for the default terminal app. Use __default__ for the app default profile. */
  "defaultProfile": string,
  /** Recent Workspaces - Show recently opened workspaces in Workspaces. */
  "showRecents": boolean,
  /** Multi-command Launch - When supported on Windows, open multiple launch commands as tabs in one Windows Terminal window. On macOS, launches always open as separate windows. */
  "singleWindowTabs": boolean,
  /** Git Branch Gate - Block launch when a target branch is set and the working tree has uncommitted changes. */
  "blockDirtyBranchSwitch": boolean
}

/** Preferences accessible in all the extension's commands */
declare type Preferences = ExtensionPreferences

declare namespace Preferences {
  /** Preferences accessible in the `open-workspace` command */
  export type OpenWorkspace = ExtensionPreferences & {}
}

declare namespace Arguments {
  /** Arguments passed to the `open-workspace` command */
  export type OpenWorkspace = {
  /** Workspace name or abbreviation */
  "query": string
}
}

