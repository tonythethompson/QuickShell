import { existsSync } from "node:fs";
import { homedir } from "node:os";
import { join } from "node:path";

export type CompanionPreset = {
  id: string;
  title: string;
  defaultArguments: string;
  candidatePaths: string[];
};

function localAppData(): string {
  return process.env.LOCALAPPDATA?.trim() || join(homedir(), "AppData", "Local");
}

function programFiles(): string {
  return process.env.ProgramFiles?.trim() || "C:\\Program Files";
}

function programFilesX86(): string {
  return process.env["ProgramFiles(x86)"]?.trim() || "C:\\Program Files (x86)";
}

function windowsDir(): string {
  return process.env.WINDIR?.trim() || "C:\\Windows";
}

/** Static catalog mirrored from Core CompanionAppCatalog (common presets + candidate paths). */
export const COMPANION_PRESETS: CompanionPreset[] = [
  {
    id: "explorer",
    title: "Windows Explorer",
    defaultArguments: "{folder}",
    candidatePaths: [join(windowsDir(), "explorer.exe")],
  },
  {
    id: "vscode",
    title: "Visual Studio Code",
    defaultArguments: ".",
    candidatePaths: [
      join(localAppData(), "Programs", "Microsoft VS Code", "Code.exe"),
      join(programFiles(), "Microsoft VS Code", "Code.exe"),
      join(programFilesX86(), "Microsoft VS Code", "Code.exe"),
    ],
  },
  {
    id: "vscode-insiders",
    title: "VS Code Insiders",
    defaultArguments: ".",
    candidatePaths: [
      join(localAppData(), "Programs", "Microsoft VS Code Insiders", "Code - Insiders.exe"),
      join(programFiles(), "Microsoft VS Code Insiders", "Code - Insiders.exe"),
    ],
  },
  {
    id: "cursor",
    title: "Cursor",
    defaultArguments: ".",
    candidatePaths: [
      join(localAppData(), "Programs", "cursor", "Cursor.exe"),
      join(localAppData(), "Programs", "Cursor", "Cursor.exe"),
    ],
  },
  {
    id: "trae",
    title: "TRAE",
    defaultArguments: ".",
    candidatePaths: [join(localAppData(), "Programs", "Trae", "Trae.exe")],
  },
  {
    id: "github-desktop",
    title: "GitHub Desktop",
    defaultArguments: "{folder}",
    candidatePaths: [
      join(localAppData(), "GitHubDesktop", "GitHubDesktop.exe"),
      join(localAppData(), "GitHub Desktop", "GitHubDesktop.exe"),
    ],
  },
  {
    id: "fork",
    title: "Fork",
    defaultArguments: "{folder}",
    candidatePaths: [join(localAppData(), "Fork", "Fork.exe"), join(programFiles(), "Fork", "Fork.exe")],
  },
  {
    id: "gitkraken",
    title: "GitKraken",
    defaultArguments: "{folder}",
    candidatePaths: [
      join(localAppData(), "gitkraken", "gitkraken.exe"),
      join(localAppData(), "GitKraken", "GitKraken.exe"),
    ],
  },
  {
    id: "notepad-plus-plus",
    title: "Notepad++",
    defaultArguments: "{folder}",
    candidatePaths: [
      join(programFiles(), "Notepad++", "notepad++.exe"),
      join(programFilesX86(), "Notepad++", "notepad++.exe"),
    ],
  },
  {
    id: "sublime",
    title: "Sublime Text",
    defaultArguments: ".",
    candidatePaths: [
      join(programFiles(), "Sublime Text", "sublime_text.exe"),
      join(programFilesX86(), "Sublime Text", "sublime_text.exe"),
    ],
  },
  {
    id: "obsidian",
    title: "Obsidian",
    defaultArguments: "{folder}",
    candidatePaths: [
      join(localAppData(), "Obsidian", "Obsidian.exe"),
      join(programFiles(), "Obsidian", "Obsidian.exe"),
    ],
  },
  {
    id: "zed",
    title: "Zed",
    defaultArguments: ".",
    candidatePaths: [
      join(localAppData(), "Programs", "Zed", "zed.exe"),
      join(localAppData(), "Programs", "Zed", "Zed.exe"),
    ],
  },
];

export function resolveCompanionPreset(presetId: string): { path: string; arguments: string } | null {
  const preset = COMPANION_PRESETS.find((entry) => entry.id === presetId);
  if (!preset) {
    return null;
  }

  for (const candidate of preset.candidatePaths) {
    if (existsSync(candidate)) {
      return { path: candidate, arguments: preset.defaultArguments };
    }
  }

  return null;
}

export function listInstalledCompanionPresets(): Array<{ id: string; title: string }> {
  return COMPANION_PRESETS.filter((preset) => resolveCompanionPreset(preset.id) !== null).map((preset) => ({
    id: preset.id,
    title: preset.title,
  }));
}
