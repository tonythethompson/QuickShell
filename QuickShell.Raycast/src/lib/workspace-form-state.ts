import { createStableId } from "./ids";
import type { Workspace } from "./schema";
import { normalizeWorkspace } from "./validation";

export type WorkspaceFormState = {
  name: string;
  abbreviation: string;
  directory: string;
  terminal: string;
  wtProfile: string;
  command: string;
  isPinned: boolean;
  runAsAdmin: boolean;
  launchLabel: string;
};

export function buildWorkspaceFromFormState(
  initialWorkspace: Workspace,
  state: WorkspaceFormState,
  options?: { showProfileField?: boolean },
): Workspace {
  const showProfileField = options?.showProfileField ?? false;
  const profile = showProfileField && state.wtProfile.trim() ? state.wtProfile.trim() : null;
  const primaryLaunchId = initialWorkspace.launches.find((entry) => entry.isEnabled)?.id
    ?? initialWorkspace.launches[0]?.id
    ?? createStableId();

  const updatedPrimary = {
    id: primaryLaunchId,
    label: state.launchLabel.trim() || state.name.trim() || "Launch",
    terminal: state.terminal,
    wtProfile: profile,
    command: state.command.trim() || null,
    runAsAdmin: state.runAsAdmin,
    isEnabled: true,
    order: 0,
    taskType: "none" as const,
  };

  const remainingLaunches = initialWorkspace.launches
    .filter((entry) => entry.id !== primaryLaunchId)
    .map((entry, index) => ({
      ...entry,
      order: index + 1,
    }));

  return normalizeWorkspace({
    ...initialWorkspace,
    name: state.name.trim(),
    abbreviation: state.abbreviation.trim() || null,
    directory: state.directory.trim(),
    terminal: state.terminal,
    wtProfile: profile,
    command: state.command.trim() || null,
    isPinned: state.isPinned,
    runAsAdmin: state.runAsAdmin,
    launches: [updatedPrimary, ...remainingLaunches],
  });
}

export function workspaceFormStateFromWorkspace(workspace: Workspace): WorkspaceFormState {
  const primaryLaunch = workspace.launches.find((entry) => entry.isEnabled) ?? workspace.launches[0];
  return {
    name: workspace.name,
    abbreviation: workspace.abbreviation ?? "",
    directory: workspace.directory,
    terminal: workspace.terminal || "default",
    wtProfile: workspace.wtProfile ?? primaryLaunch?.wtProfile ?? "",
    command: primaryLaunch?.command ?? workspace.command ?? "",
    isPinned: workspace.isPinned,
    runAsAdmin: workspace.runAsAdmin || primaryLaunch?.runAsAdmin || false,
    launchLabel: primaryLaunch?.label ?? "Launch",
  };
}

export function filterWorkspacesForEdit(workspaces: Workspace[], query: string): Workspace[] {
  const trimmed = query.trim().toLowerCase();
  if (!trimmed) {
    return [...workspaces].sort((left, right) => left.name.localeCompare(right.name, undefined, { sensitivity: "base" }));
  }

  return workspaces
    .filter((workspace) => {
      const haystacks = [
        workspace.name,
        workspace.abbreviation ?? "",
        workspace.directory,
        ...workspace.launches.map((launch) => `${launch.label} ${launch.command ?? ""}`),
      ];
      return haystacks.some((value) => value.toLowerCase().includes(trimmed));
    })
    .sort((left, right) => left.name.localeCompare(right.name, undefined, { sensitivity: "base" }));
}

export function additionalLaunchCount(workspace: Workspace): number {
  return Math.max(0, workspace.launches.filter((entry) => entry.isEnabled).length - 1);
}
