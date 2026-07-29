import { execFile } from "node:child_process";
import { existsSync } from "node:fs";
import path from "node:path";
import { promisify } from "node:util";
import { buildProjectSetupSuggestions, type WorkspaceSetupTask } from "./project-setup-suggestion";

const execFileAsync = promisify(execFile);

/** Cap setup seed so leftover pills remain available in Actions (CmdPal-shaped). */
export const MAX_SETUP_SEED_TASKS = 4;

const PREFERRED_SEED_TASK_TYPES = new Set(["frontend", "api", "services", "test", "build"]);
const PREFERRED_SEED_COMMAND_HINTS = ["dev", "start", "test", "build", "watch", "run"];

export type SuggestionPill = {
  command: string;
  taskType: string;
  typeTitle: string;
  displayTitle: string;
  tooltip: string;
};

export type SuggestionResponse = {
  generation: number;
  pills: SuggestionPill[];
};

export type WorkspaceSuggestionResult = {
  source: "suggest" | "local";
  tasks: WorkspaceSetupTask[];
  pills: SuggestionPill[];
};

export function resolveSuggestExecutable(assetsPath?: string): string | null {
  const fromEnv = process.env.QUICKSHELL_SUGGEST_EXE?.trim();
  if (fromEnv) {
    return fromEnv;
  }

  const packaged = path.join(assetsPath ?? path.join(__dirname, "..", "..", "assets"), "QuickShell.Suggest.exe");
  return packaged;
}

export function buildSuggestCommandArgs(directory: string, usedCommands: string[], generation: number): string[] {
  const args = ["suggest", "--dir", directory, "--generation", String(generation)];
  for (const command of usedCommands) {
    const trimmed = command.trim();
    if (trimmed.length > 0) {
      args.push("--used", trimmed);
    }
  }
  return args;
}

export function pillsToSetupTasks(pills: SuggestionPill[]): WorkspaceSetupTask[] {
  const tasks: WorkspaceSetupTask[] = [];
  const seen = new Set<string>();
  for (const pill of pills) {
    const command = pill.command?.trim();
    if (!command) {
      continue;
    }
    const key = command.toLowerCase();
    if (seen.has(key)) {
      continue;
    }
    seen.add(key);
    tasks.push({
      label: (pill.displayTitle || pill.typeTitle || command).trim() || command,
      command,
      taskType: pill.taskType?.trim() || "none",
    });
  }
  return tasks;
}

export function isPreferredSetupSeedPill(pill: SuggestionPill): boolean {
  const taskType = pill.taskType?.trim().toLowerCase() ?? "";
  if (PREFERRED_SEED_TASK_TYPES.has(taskType)) {
    return true;
  }

  const command = pill.command?.trim().toLowerCase() ?? "";
  if (!command) {
    return false;
  }

  return PREFERRED_SEED_COMMAND_HINTS.some(
    (hint) =>
      command === hint ||
      command.endsWith(` ${hint}`) ||
      command.includes(` run ${hint}`) ||
      command.includes(` task ${hint}`) ||
      command.startsWith(`${hint} `),
  );
}

/**
 * Split ranked Suggest pills into a short setup seed and leftover Actions pills.
 * Preferred task types / setup-like commands are taken first, capped at MAX_SETUP_SEED_TASKS.
 */
export function splitPillsIntoSeedAndLeftover(
  pills: SuggestionPill[],
  maxSeed = MAX_SETUP_SEED_TASKS,
): { tasks: WorkspaceSetupTask[]; leftoverPills: SuggestionPill[] } {
  const usable = pills.filter((pill) => pill.command?.trim());
  if (usable.length === 0) {
    return { tasks: [], leftoverPills: [] };
  }

  const seedPills: SuggestionPill[] = [];
  const leftover: SuggestionPill[] = [];
  const seedCommands = new Set<string>();

  for (const pill of usable) {
    const key = pill.command.trim().toLowerCase();
    if (seedPills.length < maxSeed && isPreferredSetupSeedPill(pill) && !seedCommands.has(key)) {
      seedPills.push(pill);
      seedCommands.add(key);
      continue;
    }
    leftover.push(pill);
  }

  if (seedPills.length === 0) {
    const take = Math.min(Math.max(1, Math.min(2, maxSeed)), usable.length);
    for (let index = 0; index < take; index += 1) {
      seedPills.push(usable[index]);
      seedCommands.add(usable[index].command.trim().toLowerCase());
    }
    return {
      tasks: pillsToSetupTasks(seedPills),
      leftoverPills: usable.filter((pill) => !seedCommands.has(pill.command.trim().toLowerCase())),
    };
  }

  return {
    tasks: pillsToSetupTasks(seedPills),
    leftoverPills: leftover.filter((pill) => !seedCommands.has(pill.command.trim().toLowerCase())),
  };
}

export async function fetchSuggestionPills(
  directory: string,
  usedCommands: string[],
  generation: number,
  assetsPath?: string,
): Promise<SuggestionResponse | null> {
  const executable = resolveSuggestExecutable(assetsPath);
  if (!executable || !existsSync(executable)) {
    return null;
  }

  const args = buildSuggestCommandArgs(directory, usedCommands, generation);

  try {
    const { stdout } = await execFileAsync(executable, args, { windowsHide: true, maxBuffer: 1024 * 1024 });
    const parsed = JSON.parse(stdout) as SuggestionResponse;
    if (parsed.generation !== generation) {
      return null;
    }

    return parsed;
  } catch {
    return null;
  }
}

/** Prefer Suggest.exe pills; fall back to local folder heuristics when the CLI is missing. */
export async function resolveWorkspaceSetupSuggestions(
  directory: string,
  usedCommands: string[] = [],
  generation = Date.now(),
  assetsPath?: string,
): Promise<WorkspaceSuggestionResult> {
  const trimmed = directory.trim();
  if (!trimmed) {
    return { source: "local", tasks: [], pills: [] };
  }

  const response = await fetchSuggestionPills(trimmed, usedCommands, generation, assetsPath);
  if (response && response.pills.length > 0) {
    const split = splitPillsIntoSeedAndLeftover(response.pills);
    return {
      source: "suggest",
      tasks: split.tasks,
      pills: split.leftoverPills,
    };
  }

  const tasks = buildProjectSetupSuggestions(trimmed);
  const asPills: SuggestionPill[] = tasks.map((task) => ({
    command: task.command,
    taskType: task.taskType?.trim() || "none",
    typeTitle: task.taskType?.trim() || "Setup",
    displayTitle: task.label,
    tooltip: task.command,
  }));
  const split = splitPillsIntoSeedAndLeftover(asPills);
  return { source: "local", tasks: split.tasks, pills: split.leftoverPills };
}
