/// <reference types="@raycast/api" />

/* 🚧 🚧 🚧
 * This file is auto-generated from the extension's manifest.
 * Do not modify manually. Instead, update the `package.json` file.
 * 🚧 🚧 🚧 */

/* eslint-disable @typescript-eslint/ban-types */

type ExtensionPreferences = {
  terminalApplication?: "system" | "wt" | "conhost" | "it";
  defaultProfile?: string;
  showRecents?: boolean;
};

/** Preferences accessible in all the extension's commands */
declare type Preferences = ExtensionPreferences;

declare namespace Preferences {
  /** Preferences accessible in the `open-workspace` command */
  export type OpenWorkspace = ExtensionPreferences;
  /** Preferences accessible in the `create-workspace` command */
  export type CreateWorkspace = ExtensionPreferences;
  /** Preferences accessible in the `edit-workspace` command */
  export type EditWorkspace = ExtensionPreferences;
  /** Preferences accessible in the `discover-git-repos` command */
  export type DiscoverGitRepos = ExtensionPreferences;
  /** Preferences accessible in the `settings` command */
  export type Settings = ExtensionPreferences;
}

declare namespace Arguments {
  /** Arguments passed to the `open-workspace` command */
  export type OpenWorkspace = {};
  /** Arguments passed to the `create-workspace` command */
  export type CreateWorkspace = {
    /** Project folder path */
    directory: string;
  };
  /** Arguments passed to the `edit-workspace` command */
  export type EditWorkspace = {
    /** Workspace ID */
    workspaceId: string;
  };
  /** Arguments passed to the `discover-git-repos` command */
  export type DiscoverGitRepos = {};
  /** Arguments passed to the `settings` command */
  export type Settings = {};
}

declare namespace LaunchContext {
  /** Launch context for the `open-workspace` command */
  export type OpenWorkspace = {
    focusWorkspaceId?: string;
    focusWorkspaceName?: string;
  };
}
