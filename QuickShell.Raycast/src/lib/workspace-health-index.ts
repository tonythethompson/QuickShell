import type { QuickShellSettings, Workspace } from "./schema";
import {
  assessWorkspaceHealthForList,
  assessWorkspaceHealthWithPortProbe,
  type PortInUseProbe,
  type WorkspaceHealthReport,
} from "./workspace-health";

export type WorkspaceHealthIndex = Map<string, WorkspaceHealthReport>;

function settingsFingerprint(settings: QuickShellSettings): string {
  return `${settings.terminalApplication}|${settings.defaultProfile}|${settings.recentWorkspaceCount}|${settings.blockDirtyBranchSwitch}`;
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
    workspace.devServerUrl ?? "",
    workspace.openDevServerOnLaunch ? "1" : "0",
    launchFingerprint,
    settingsFingerprint(settings),
  ].join(":");
}

export function buildWorkspaceHealthIndex(
  workspaces: Workspace[],
  settings: QuickShellSettings,
  isPortInUse?: PortInUseProbe,
): WorkspaceHealthIndex {
  const index: WorkspaceHealthIndex = new Map();
  for (const workspace of workspaces) {
    index.set(
      workspaceHealthFingerprint(workspace, settings),
      assessWorkspaceHealthForList(workspace, settings, { isPortInUse }),
    );
  }
  return index;
}

export async function buildWorkspaceHealthIndexWithPorts(
  workspaces: Workspace[],
  settings: QuickShellSettings,
): Promise<WorkspaceHealthIndex> {
  const index: WorkspaceHealthIndex = new Map();
  for (const workspace of workspaces) {
    index.set(
      workspaceHealthFingerprint(workspace, settings),
      await assessWorkspaceHealthWithPortProbe(workspace, settings, {
        includeLaunchPlan: false,
        includeDirectoryExists: true,
      }),
    );
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

export { assessWorkspaceHealthForLaunch } from "./workspace-health";
