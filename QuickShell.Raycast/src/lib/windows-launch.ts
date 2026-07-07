import type { LaunchEntry, QuickShellSettings, Workspace } from "./schema";

export type LaunchTargetKind = "wt" | "powershell" | "pwsh" | "cmd" | "wsl";

export type ResolvedLaunchTarget = {
  kind: LaunchTargetKind;
  hostExecutable: string;
  profileOrDistro?: string | null;
  displayName: string;
};

export type LaunchPlanEntry = {
  workspace: Workspace;
  launch: LaunchEntry;
  target: ResolvedLaunchTarget;
  directory: string;
  command?: string | null;
  runAsAdmin: boolean;
};

export type LaunchPlan = {
  entries: LaunchPlanEntry[];
  groupedArguments: string[];
  errors: string[];
};

const PACKAGE_MANAGER_COMMANDS = new Set([
  "npm",
  "pnpm",
  "yarn",
  "bun",
  "npx",
  "dotnet",
  "cargo",
  "go",
]);

export function resolveTerminalForLaunch(
  launch: LaunchEntry,
  settings: QuickShellSettings,
  previousTerminal?: string,
): { terminal: string; wtProfile?: string | null } {
  if (launch.terminal === "same-as-previous" && previousTerminal) {
    return { terminal: previousTerminal, wtProfile: launch.wtProfile };
  }

  if (launch.terminal === "default") {
    return {
      terminal:
        settings.terminalApplication === "system"
          ? "wt"
          : settings.terminalApplication,
      wtProfile:
        settings.defaultProfile === "__default__"
          ? null
          : settings.defaultProfile,
    };
  }

  return { terminal: launch.terminal, wtProfile: launch.wtProfile };
}

export function resolveLaunchTarget(
  terminal: string,
  wtProfile?: string | null,
): ResolvedLaunchTarget {
  switch (terminal) {
    case "wt":
    case "it":
      return {
        kind: "wt",
        hostExecutable: terminal === "it" ? "wt.exe" : "wt.exe",
        profileOrDistro: wtProfile,
        displayName: wtProfile
          ? `Windows Terminal (${wtProfile})`
          : "Windows Terminal",
      };
    case "powershell":
      return {
        kind: "powershell",
        hostExecutable: "powershell.exe",
        displayName: "Windows PowerShell",
      };
    case "pwsh":
      return {
        kind: "pwsh",
        hostExecutable: "pwsh.exe",
        displayName: "PowerShell",
      };
    case "cmd":
      return {
        kind: "cmd",
        hostExecutable: "cmd.exe",
        displayName: "Command Prompt",
      };
    case "wsl":
      return {
        kind: "wsl",
        hostExecutable: "wsl.exe",
        profileOrDistro: wtProfile,
        displayName: wtProfile ? `WSL (${wtProfile})` : "WSL",
      };
    default:
      return {
        kind: "wt",
        hostExecutable: "wt.exe",
        profileOrDistro: wtProfile,
        displayName: "Windows Terminal",
      };
  }
}

export function escapeWindowsArgument(value: string): string {
  if (!/[ \t"]/g.test(value)) {
    return value;
  }
  return `"${value.replace(/"/g, '\\"')}"`;
}

export function buildSetLocationCommand(directory: string): string {
  const normalized = directory.replace(/'/g, "''");
  return `Set-Location -LiteralPath '${normalized}'`;
}

export function buildCmdChangeDirectory(directory: string): string {
  return `cd /d ${escapeWindowsArgument(directory)}`;
}

export function buildLaunchArguments(entry: LaunchPlanEntry): string[] {
  const args: string[] = [];
  const { target, directory, command } = entry;

  if (target.kind === "wt") {
    args.push("-w", "0");
    if (target.profileOrDistro) {
      args.push("-p", target.profileOrDistro);
    }
    args.push("-d", directory);
    if (command) {
      args.push(command);
    }
    return args;
  }

  if (target.kind === "wsl") {
    if (target.profileOrDistro) {
      args.push("-d", target.profileOrDistro);
    }
    const wslCommand = command
      ? `cd ${shellQuoteForBash(directory)} && ${command}`
      : `cd ${shellQuoteForBash(directory)} && exec $SHELL -l`;
    args.push("--", "bash", "-lc", wslCommand);
    return args;
  }

  if (target.kind === "cmd") {
    const cmdParts = [buildCmdChangeDirectory(directory)];
    if (command) {
      cmdParts.push("&&", command);
    }
    args.push("/k", cmdParts.join(" "));
    return args;
  }

  const psParts = [buildSetLocationCommand(directory)];
  if (command) {
    psParts.push("; " + command);
  }
  args.push("-NoExit", "-Command", psParts.join(""));
  return args;
}

function shellQuoteForBash(value: string): string {
  return `'${value.replace(/'/g, `'\\''`)}'`;
}

function usesPackageManager(command: string): boolean {
  const firstToken = command.trim().split(/\s+/)[0]?.toLowerCase() ?? "";
  return PACKAGE_MANAGER_COMMANDS.has(firstToken);
}

export function shouldRouteThroughCmd(
  command: string | null | undefined,
): boolean {
  if (!command) {
    return false;
  }
  return usesPackageManager(command);
}

export function buildWorkspaceLaunchPlan(
  workspace: Workspace,
  settings: QuickShellSettings,
): LaunchPlan {
  const errors: string[] = [];
  const directory = workspace.directory.trim();
  if (!directory) {
    errors.push("Workspace directory is required.");
  }

  const enabledLaunches = workspace.launches.filter(
    (launch) => launch.isEnabled,
  );
  if (enabledLaunches.length === 0) {
    errors.push("No enabled launch entries.");
  }

  const entries: LaunchPlanEntry[] = [];
  let previousTerminal: string | undefined;

  for (const launch of enabledLaunches.sort(
    (left, right) => left.order - right.order,
  )) {
    const resolved = resolveTerminalForLaunch(
      launch,
      settings,
      previousTerminal,
    );
    previousTerminal = resolved.terminal;
    const target = resolveLaunchTarget(resolved.terminal, resolved.wtProfile);
    const command = launch.command?.trim() || null;

    entries.push({
      workspace,
      launch,
      target,
      directory,
      command,
      runAsAdmin: launch.runAsAdmin || workspace.runAsAdmin,
    });
  }

  const groupedArguments = buildGroupedWindowsTerminalArguments(entries);

  return { entries, groupedArguments, errors };
}

export function buildGroupedWindowsTerminalArguments(
  entries: LaunchPlanEntry[],
): string[] {
  if (entries.length === 0) {
    return [];
  }

  const args: string[] = [];
  for (let index = 0; index < entries.length; index++) {
    const entry = entries[index];
    if (index > 0) {
      args.push(";", "new-tab");
    }
    args.push(...buildLaunchArguments(entry));
  }
  return args;
}

export function formatLaunchPlanSummary(plan: LaunchPlan): string {
  if (plan.errors.length > 0) {
    return plan.errors.join(" ");
  }
  if (plan.entries.length === 0) {
    return "No launch entries.";
  }
  if (plan.entries.length === 1) {
    const entry = plan.entries[0];
    return `${entry.target.displayName} → ${entry.directory}`;
  }
  return `${plan.entries.length} launches in ${plan.entries[0].target.displayName}`;
}
