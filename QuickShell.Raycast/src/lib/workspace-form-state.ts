import { createStableId } from "./ids";
import { suggestionLabelForCommand } from "./project-setup-suggestion";
import type { LaunchEntry, Workspace } from "./schema";
import { normalizeWorkspace } from "./validation";

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
};

function usesSharedLaunchControls(state: WorkspaceFormState): boolean {
  return state.launches.length === 1;
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

export function buildWorkspaceFromFormState(
  initialWorkspace: Workspace,
  state: WorkspaceFormState,
): Workspace {
  const launches: LaunchEntry[] = state.launches
    .filter((row) => row.command.trim())
    .map((row, index) => ({
      id: row.id || createStableId(),
      label: row.label.trim() || suggestionLabelForCommand(row.command, `Launch ${index + 1}`),
      terminal: terminalForLaunchRow(row, state),
      wtProfile: wtProfileForLaunchRow(row, state),
      command: row.command.trim() || null,
      runAsAdmin: usesSharedLaunchControls(state) ? state.runAsAdmin : row.runAsAdmin || state.runAsAdmin,
      isEnabled: row.isEnabled,
      order: index,
      taskType: "none",
    }));

  const primary = launches.find((entry) => entry.isEnabled) ?? launches[0];

  return normalizeWorkspace({
    ...initialWorkspace,
    name: state.name.trim(),
    abbreviation: state.abbreviation.trim() || null,
    directory: state.directory.trim(),
    terminal: primary?.terminal ?? state.terminal,
    wtProfile: primary?.wtProfile ?? state.wtProfile ?? null,
    command: primary?.command ?? null,
    isPinned: state.isPinned,
    runAsAdmin: state.runAsAdmin,
    launches,
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
    runAsAdmin: workspace.runAsAdmin,
    launches,
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

