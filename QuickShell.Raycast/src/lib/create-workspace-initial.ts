import { deriveAbbreviationFromName, deriveNameFromDirectory } from "./directory-helpers";
import { createStableId } from "./ids";
import { buildProjectSetupSuggestions } from "./project-setup-suggestion";
import type { Workspace } from "./schema";
import { normalizeWorkspace } from "./validation";
import { launchRowsFromSuggestions } from "./workspace-form-state";

export function createBlankWorkspace(): Workspace {
  const id = createStableId();
  return normalizeWorkspace({
    id,
    name: "",
    abbreviation: null,
    directory: "",
    isPinned: false,
    pinOrder: null,
    lastUsedUtc: null,
    terminal: "default",
    wtProfile: null,
    command: null,
    runAsAdmin: false,
    launches: [
      {
        id: createStableId(),
        label: "Launch",
        terminal: "default",
        wtProfile: null,
        command: null,
        runAsAdmin: false,
        isEnabled: true,
        order: 0,
        taskType: "none",
      },
    ],
  });
}

export function createWorkspaceFromDirectory(directory: string | undefined): Workspace {
  const trimmed = directory?.trim();
  if (!trimmed) {
    return createBlankWorkspace();
  }

  const name = deriveNameFromDirectory(trimmed);
  const abbreviation = name ? deriveAbbreviationFromName(name) : null;
  const suggestions = buildProjectSetupSuggestions(trimmed);
  const rows = launchRowsFromSuggestions(suggestions);

  const launches =
    rows.length > 0
      ? rows.map((row, index) => ({
          id: row.id,
          label: row.label,
          terminal: row.terminal,
          wtProfile: row.wtProfile ?? null,
          command: row.command.trim() || null,
          runAsAdmin: row.runAsAdmin,
          isEnabled: index === 0,
          order: index,
          taskType: row.taskType,
        }))
      : createBlankWorkspace().launches;

  return normalizeWorkspace({
    ...createBlankWorkspace(),
    name,
    abbreviation,
    directory: trimmed,
    launches,
  });
}
