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

function workspaceHealthFingerprint(workspace: Workspace, settings: QuickShellSettings): string {
  const launchFingerprint = workspace.launches
    .map(
      (launch) =>
        `${launch.id}:${launch.isEnabled}:${launch.command ?? ""}:${launch.terminal}:${launch.wtProfile ?? ""}:${launch.runAsAdmin}`,
    )
    .join("|");

  return [
    workspace.id,
    workspace.name,
    workspace.directory,
    workspace.companionAppPath ?? "",
    workspace.openCompanionAppOnLaunch ? "1" : "0",
    launchFingerprint,
    settingsFingerprint(settings),
  ].join(":");
}

export function buildWorkspaceHealthIndex(workspaces: Workspace[], settings: QuickShellSettings): WorkspaceHealthIndex {
  const index: WorkspaceHealthIndex = new Map();

  for (const workspace of workspaces) {
    const key = workspaceHealthFingerprint(workspace, settings);
    index.set(key, assessWorkspaceHealthForList(workspace, settings));
  }

  return index;
}

export function lookupWorkspaceHealth(
  index: WorkspaceHealthIndex,
  workspace: Workspace,
  settings: QuickShellSettings,
): WorkspaceHealthReport {
  const key = workspaceHealthFingerprint(workspace, settings);
  return index.get(key) ?? assessWorkspaceHealthForList(workspace, settings);
}

export { assessWorkspaceHealthForLaunch as assessWorkspaceHealthForLaunch };
