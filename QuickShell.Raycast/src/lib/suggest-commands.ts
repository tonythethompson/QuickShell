import { execFile } from "node:child_process";
import path from "node:path";
import { promisify } from "node:util";

const execFileAsync = promisify(execFile);

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

function resolveSuggestExecutable(): string | null {
  const fromEnv = process.env.QUICKSHELL_SUGGEST_EXE?.trim();
  if (fromEnv) {
    return fromEnv;
  }

  return path.join(__dirname, "..", "..", "bin", "QuickShell.Suggest.exe");
}

export async function fetchSuggestionPills(
  directory: string,
  usedCommands: string[],
  generation: number,
): Promise<SuggestionResponse | null> {
  const executable = resolveSuggestExecutable();
  if (!executable) {
    return null;
  }

  const args = ["suggest", "--dir", directory, "--generation", String(generation)];
  for (const command of usedCommands) {
    const trimmed = command.trim();
    if (trimmed.length > 0) {
      args.push("--used", trimmed);
    }
  }

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
