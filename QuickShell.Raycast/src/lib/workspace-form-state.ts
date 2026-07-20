import { createStableId } from "./ids";
import { suggestionLabelForCommand } from "./project-setup-suggestion";
import type { CompanionAppEntry, LaunchEntry, Workspace } from "./schema";
import { normalizeCompanionApps, normalizeWorkspace } from "./validation";

export type LaunchFormRow = {
  id: string;
  command: string;
  terminal: string;
  wtProfile?: string | null;
  runAsAdmin: boolean;
  isEnabled: boolean;
  label: string;
};

export type WorkspaceFormState = {
  name: string;
  abbreviation: string;
  directory: string;
  terminal: string;
  wtProfile?: string | null;
  isPinned: boolean;
  runAsAdmin: boolean;
  launches: LaunchFormRow[];
  devServerUrl: string;
  openDevServerOnLaunch: boolean;
  repoUrl: string;
  openCompanionAppOnLaunch: boolean;
  companionAppPath: string;
  companionAppArguments: string;
};

function savableLaunchRowCount(state: WorkspaceFormState): number {
  return state.launches.filter((row) => row.command.trim()).length;
}

function usesSharedLaunchControls(state: WorkspaceFormState): boolean {
  return savableLaunchRowCount(state) <= 1;
}

function terminalForLaunchRow(row: LaunchFormRow, state: WorkspaceFormState): string {
  if (usesSharedLaunchControls(state)) {
    return state.terminal || "default";
  }

  return row.terminal || state.terminal || "default";
}

function wtProfileForLaunchRow(row: LaunchFormRow, state: WorkspaceFormState): string | null {
  if (usesSharedLaunchControls(state)) {
    return state.wtProfile ?? null;
  }

  return row.wtProfile ?? state.wtProfile ?? null;
}

export function buildWorkspaceFromFormState(initialWorkspace: Workspace, state: WorkspaceFormState): Workspace {
  const launches: LaunchEntry[] = state.launches
    .filter((row) => row.command.trim())
    .map((row, index) => ({
      id: row.id || createStableId(),
      label: row.label.trim() || suggestionLabelForCommand(row.command, `Launch ${index + 1}`),
      terminal: terminalForLaunchRow(row, state),
      wtProfile: wtProfileForLaunchRow(row, state),
      command: row.command || null,
      runAsAdmin: usesSharedLaunchControls(state) ? state.runAsAdmin : row.runAsAdmin || state.runAsAdmin,
      isEnabled: row.isEnabled,
      order: index,
      taskType: "none",
    }));

  const primary = launches.find((entry) => entry.isEnabled) ?? launches[0];

  // Form still edits the primary companion; keep additional companions from the existing workspace.
  const existingCompanions = normalizeCompanionApps(initialWorkspace);
  const additionalCompanions = existingCompanions.slice(1);
  const primaryPath = state.companionAppPath?.trim() || null;
  const companionApps: CompanionAppEntry[] = [];
  if (primaryPath) {
    companionApps.push({
      id: existingCompanions[0]?.id || createStableId(),
      path: primaryPath,
      arguments: state.companionAppArguments || null,
      openOnLaunch: state.openCompanionAppOnLaunch ?? false,
      order: 0,
    });
  }
  companionApps.push(
    ...additionalCompanions.map((entry, index) => ({
      ...entry,
      order: companionApps.length + index,
    })),
  );

  return normalizeWorkspace({
    ...initialWorkspace,
    name: state.name.trim(),
    abbreviation: state.abbreviation.trim() || null,
    directory: state.directory.trim(),
    terminal: primary?.terminal ?? state.terminal,
    wtProfile: primary?.wtProfile ?? state.wtProfile ?? null,
    command: primary?.command ?? null,
    isPinned: state.isPinned,
    runAsAdmin: state.runAsAdmin || launches.some((launch) => launch.runAsAdmin),
    launches,
    companionApps,
    devServerUrl: state.devServerUrl?.trim() || null,
    openDevServerOnLaunch: state.openDevServerOnLaunch ?? false,
    repoUrl: state.repoUrl?.trim() || null,
    openCompanionAppOnLaunch: state.openCompanionAppOnLaunch ?? false,
    companionAppPath: primaryPath,
    companionAppArguments: state.companionAppArguments?.trim() || null,
  });
}

export function workspaceFormStateFromWorkspace(workspace: Workspace): WorkspaceFormState {
  const launches = workspace.launches.length
    ? workspace.launches.map((launch) => ({
        id: launch.id,
        command: launch.command ?? "",
        terminal: launch.terminal || workspace.terminal || "default",
        wtProfile: launch.wtProfile ?? workspace.wtProfile ?? null,
        runAsAdmin: launch.runAsAdmin,
        isEnabled: launch.isEnabled,
        label: launch.label,
      }))
    : [
        {
          id: createStableId(),
          command: workspace.command ?? "",
          terminal: workspace.terminal || "default",
          wtProfile: workspace.wtProfile ?? null,
          runAsAdmin: workspace.runAsAdmin,
          isEnabled: true,
          label: workspace.name || "Launch",
        },
      ];

  const primary = launches.find((launch) => launch.isEnabled) ?? launches[0];

  return {
    name: workspace.name,
    abbreviation: workspace.abbreviation ?? "",
    directory: workspace.directory,
    terminal: primary?.terminal ?? workspace.terminal ?? "default",
    wtProfile: primary?.wtProfile ?? workspace.wtProfile ?? null,
    isPinned: workspace.isPinned,
    runAsAdmin: workspace.runAsAdmin || launches.some((launch) => launch.runAsAdmin),
    launches,
    devServerUrl: workspace.devServerUrl ?? "",
    openDevServerOnLaunch: Boolean(workspace.openDevServerOnLaunch),
    repoUrl: workspace.repoUrl ?? "",
    openCompanionAppOnLaunch: Boolean(workspace.openCompanionAppOnLaunch),
    companionAppPath: workspace.companionAppPath ?? "",
    companionAppArguments: workspace.companionAppArguments ?? "",
  };
}

export function launchRowsFromSuggestions(
  suggestions: Array<{ label: string; command: string }>,
  terminal = "default",
): LaunchFormRow[] {
  return suggestions.map((suggestion) => ({
    id: createStableId(),
    command: suggestion.command,
    terminal,
    wtProfile: null,
    runAsAdmin: false,
    isEnabled: true,
    label: suggestion.label,
  }));
}

export function filterWorkspacesForEdit(workspaces: Workspace[], query: string): Workspace[] {
  const trimmed = query.trim().toLowerCase();
  if (!trimmed) {
    return [...workspaces].sort((left, right) =>
      left.name.localeCompare(right.name, undefined, { sensitivity: "base" }),
    );
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

export type PillKeyPayload = {
  taskType: string;
  command: string;
};

export function encodePillKey(pill: PillKeyPayload): string {
  return JSON.stringify({ taskType: pill.taskType, command: pill.command });
}

export function decodePillKey(key: string): PillKeyPayload | undefined {
  try {
    const parsed = JSON.parse(key) as Partial<PillKeyPayload>;
    if (typeof parsed.taskType !== "string" || typeof parsed.command !== "string") {
      return undefined;
    }

    return { taskType: parsed.taskType, command: parsed.command };
  } catch {
    return undefined;
  }
}
