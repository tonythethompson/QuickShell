import { spawn } from "node:child_process";
import type { CompanionAppEntry, Workspace } from "./schema";
import { getOpenOnLaunchCompanions } from "./validation";
import { escapeWindowsArgument } from "./windows-launch";

export type PostLaunchResult = {
  companionOpened: boolean;
  devServerOpened: boolean;
  warnings: string[];
};

export type OpenUrlFn = (url: string) => Promise<void>;

const defaultOpenUrl: OpenUrlFn = async (url) => {
  await spawnDetached(process.platform === "win32" ? "cmd.exe" : "open", buildOpenUrlArgs(url));
};

export async function runPostLaunchActions(
  workspace: Workspace,
  options?: { includeCompanion?: boolean; includeDevServer?: boolean; openUrl?: OpenUrlFn },
): Promise<PostLaunchResult> {
  const includeCompanion = options?.includeCompanion ?? true;
  const includeDevServer = options?.includeDevServer ?? true;
  const openUrl = options?.openUrl ?? defaultOpenUrl;
  const warnings: string[] = [];
  let companionOpened = false;
  let devServerOpened = false;

  if (includeCompanion) {
    const companions = getOpenOnLaunchCompanions(workspace);
    for (const companion of companions) {
      try {
        await launchCompanionEntry(companion, workspace.directory);
        companionOpened = true;
      } catch (error) {
        const message = error instanceof Error ? error.message : "Companion app launch failed.";
        warnings.push(message);
      }
    }
  }

  if (includeDevServer && workspace.openDevServerOnLaunch && workspace.devServerUrl?.trim()) {
    try {
      await openUrl(workspace.devServerUrl.trim());
      devServerOpened = true;
    } catch (error) {
      const message = error instanceof Error ? error.message : "Dev server link failed.";
      warnings.push(message);
    }
  }

  return { companionOpened, devServerOpened, warnings };
}

async function launchCompanionEntry(entry: CompanionAppEntry, directory: string): Promise<void> {
  const executable = entry.path?.trim();
  if (!executable) {
    throw new Error("Companion app path is empty.");
  }

  const args = buildCompanionArguments(entry.arguments, directory);
  await spawnDetached(executable, args);
}

export function buildCompanionArguments(rawArguments: string | null | undefined, directory: string): string[] {
  if (!rawArguments?.trim()) {
    return [directory];
  }

  return tokenizeCompanionArguments(rawArguments)
    .map((token) => token.replace(/\{folder\}/gi, directory).replace(/^\.$/i, directory))
    .filter(Boolean);
}

function tokenizeCompanionArguments(raw: string): string[] {
  const tokens: string[] = [];
  let current = "";
  let inQuotes = false;

  for (const char of raw) {
    if (inQuotes) {
      if (char === '"') {
        inQuotes = false;
      } else {
        current += char;
      }
      continue;
    }

    if (char === '"') {
      inQuotes = true;
      continue;
    }

    if (/\s/.test(char)) {
      if (current) {
        tokens.push(current);
        current = "";
      }
      continue;
    }

    current += char;
  }

  if (current) {
    tokens.push(current);
  }

  return tokens;
}

function buildOpenUrlArgs(url: string): string[] {
  if (process.platform === "win32") {
    return ["/c", "start", "", escapeWindowsArgument(url)];
  }
  return [url];
}

function spawnDetached(executable: string, args: string[]): Promise<void> {
  return new Promise((resolve, reject) => {
    const child = spawn(executable, args, {
      detached: true,
      stdio: "ignore",
      shell: false,
      windowsHide: false,
    });
    child.once("error", reject);
    child.once("spawn", () => {
      child.unref();
      resolve();
    });
  });
}
