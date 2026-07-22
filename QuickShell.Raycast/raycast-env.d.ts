/// <reference types="@raycast/api">

/* 🚧 🚧 🚧
 * This file is auto-generated from the extension's manifest.
 * Do not modify manually. Instead, update the `package.json` file.
 * 🚧 🚧 🚧 */

/* eslint-disable @typescript-eslint/ban-types */

type ExtensionPreferences = {
  /** Default Terminal App - Terminal application used when a workspace launch uses the QuickShell default. */
  "terminalApplication": "system" | "wt" | "conhost" | "it",
  /** Default Profile - Profile name for the default terminal app. Use __default__ for the app default profile. */
  "defaultProfile": string,
  /** Recent Workspaces - Show recently opened workspaces in Open Workspace. */
  "showRecents": boolean,
  /** Multi-command Launch - When supported, open multiple launch commands as tabs in one Windows Terminal window instead of separate windows. */
  "singleWindowTabs": boolean,
  /** Git Branch Gate - Block launch when a target branch is set and the working tree has uncommitted changes. */
  "blockDirtyBranchSwitch": boolean
}

/** Preferences accessible in all the extension's commands */
declare type Preferences = ExtensionPreferences

declare namespace Preferences {
  /** Preferences accessible in the `open-workspace` command */
  export type OpenWorkspace = ExtensionPreferences & {}
  /** Preferences accessible in the `create-workspace` command */
  export type CreateWorkspace = ExtensionPreferences & {}
  /** Preferences accessible in the `edit-workspace` command */
  export type EditWorkspace = ExtensionPreferences & {}
  /** Preferences accessible in the `discover-git-repos` command */
  export type DiscoverGitRepos = ExtensionPreferences & {}
  /** Preferences accessible in the `settings` command */
  export type Settings = ExtensionPreferences & {}
}

declare namespace Arguments {
  /** Arguments passed to the `open-workspace` command */
  export type OpenWorkspace = {}
  /** Arguments passed to the `create-workspace` command */
  export type CreateWorkspace = {
  /** Project folder path */
  "directory": string
}
  /** Arguments passed to the `edit-workspace` command */
  export type EditWorkspace = {
  /** Workspace ID */
  "workspaceId": string
}
  /** Arguments passed to the `discover-git-repos` command */
  export type DiscoverGitRepos = {}
  /** Arguments passed to the `settings` command */
  export type Settings = {}
}

