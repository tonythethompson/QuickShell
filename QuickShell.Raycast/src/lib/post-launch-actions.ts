import { spawn } from "node:child_process";
import type { AuthorizedCompanionEffect, AuthorizedPostLaunchEffectsPlan } from "./security";

export type PostLaunchResult = {
  companionOpened: boolean;
  devServerOpened: boolean;
  warnings: string[];
};

export type OpenUrlFn = (url: string) => Promise<void>;
export type LaunchCompanionFn = (effect: AuthorizedCompanionEffect) => Promise<void>;

const defaultOpenUrl: OpenUrlFn = async (url) => {
  const invocation = buildOpenUrlInvocation(url);
  await spawnDetached(invocation.executable, invocation.args);
};

export function buildOpenUrlInvocation(url: string): { executable: string; args: string[] } {
  return { executable: "explorer.exe", args: [url] };
}

export async function runPostLaunchActions(
  plan: AuthorizedPostLaunchEffectsPlan,
  options?: { openUrl?: OpenUrlFn; launchCompanion?: LaunchCompanionFn },
): Promise<PostLaunchResult> {
  const openUrl = options?.openUrl ?? defaultOpenUrl;
  const launchCompanion = options?.launchCompanion ?? launchCompanionEffect;
  const warnings: string[] = [];
  let companionOpened = false;
  let devServerOpened = false;

  for (const companion of plan.companions) {
    try {
      await launchCompanion(companion);
      companionOpened = true;
    } catch (error) {
      const message = error instanceof Error ? error.message : "Companion app launch failed.";
      warnings.push(message);
    }
  }

  if (plan.devServerUrl) {
    try {
      await openUrl(plan.devServerUrl);
      devServerOpened = true;
    } catch (error) {
      const message = error instanceof Error ? error.message : "Dev server link failed.";
      warnings.push(message);
    }
  }

  return { companionOpened, devServerOpened, warnings };
}

async function launchCompanionEffect(effect: AuthorizedCompanionEffect): Promise<void> {
  const args = buildCompanionArguments(effect.arguments, effect.workingDirectory);
  await spawnDetached(effect.executablePath, args);
}

export function buildCompanionArguments(rawArguments: string | null | undefined, directory: string): string[] {
  if (!rawArguments || !rawArguments.trim()) {
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
