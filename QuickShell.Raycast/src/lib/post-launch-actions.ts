import { spawn } from "node:child_process";
import type { Workspace } from "./schema";

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

  if (includeCompanion && workspace.openCompanionAppOnLaunch && workspace.companionAppPath?.trim()) {
    try {
      await launchCompanionApp(workspace);
      companionOpened = true;
    } catch (error) {
      const message = error instanceof Error ? error.message : "Companion app launch failed.";
      warnings.push(message);
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

async function launchCompanionApp(workspace: Workspace): Promise<void> {
  const executable = workspace.companionAppPath?.trim();
  if (!executable) {
    throw new Error("Companion app path is empty.");
  }

  const args = buildCompanionArguments(workspace.companionAppArguments, workspace.directory);
  await spawnDetached(executable, args);
}

export function buildCompanionArguments(rawArguments: string | null | undefined, directory: string): string[] {
  if (!rawArguments?.trim()) {
    return [directory];
  }

  return rawArguments
    .split(/\s+/)
    .map((token) => token.replace(/\{folder\}/gi, directory).replace(/^\.$/i, directory))
    .filter(Boolean);
}

function buildOpenUrlArgs(url: string): string[] {
  if (process.platform === "win32") {
    return ["/c", "start", "", url];
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
