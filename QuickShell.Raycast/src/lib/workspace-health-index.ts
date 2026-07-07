import type { QuickShellSettings, Workspace } from "./schema";
import {
  assessWorkspaceHealthForList,
  assessWorkspaceHealthForLaunch,
  type WorkspaceHealthReport,
} from "./workspace-health";

export type WorkspaceHealthIndex = Map<string, WorkspaceHealthReport>;

function settingsFingerprint(settings: QuickShellSettings): string {
  return `${settings.terminalApplication}|${settings.defaultProfile}|${settings.recentWorkspaceCount}`;
}

export function buildWorkspaceHealthIndex(
  workspaces: Workspace[],
  settings: QuickShellSettings,
): WorkspaceHealthIndex {
  const index: WorkspaceHealthIndex = new Map();
  const settingsKey = settingsFingerprint(settings);

  for (const workspace of workspaces) {
    const key = `${workspace.id}:${workspace.directory}:${settingsKey}`;
    index.set(key, assessWorkspaceHealthForList(workspace, settings));
  }

  return index;
}

export function lookupWorkspaceHealth(
  index: WorkspaceHealthIndex,
  workspace: Workspace,
  settings: QuickShellSettings,
): WorkspaceHealthReport {
  const settingsKey = settingsFingerprint(settings);
  const key = `${workspace.id}:${workspace.directory}:${settingsKey}`;
  return index.get(key) ?? assessWorkspaceHealthForList(workspace, settings);
}

export { assessWorkspaceHealthForLaunch as assessWorkspaceHealthForLaunch };
