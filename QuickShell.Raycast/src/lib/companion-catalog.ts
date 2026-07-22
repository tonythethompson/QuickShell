import { existsSync } from "node:fs";
import { basename, join } from "node:path";
import { homedir } from "node:os";

export type CompanionPreset = {
  id: string;
  title: string;
  defaultArguments: string;
  candidatePaths: string[];
};

/** Mirrors Core CompanionAppCatalog.PresetNone / PresetCustom. */
export const COMPANION_PRESET_NONE = "none";
export const COMPANION_PRESET_CUSTOM = "custom";
export const COMPANION_CHOICE_TITLE_NONE = "No companion app";
export const COMPANION_CHOICE_TITLE_CUSTOM = "Custom app";

export type CompanionFormChoice = {
  id: string;
  title: string;
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

let cachedCompanionChoices: CompanionFormChoice[] | null = null;

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

/** CmdPal-style dropdown: None → installed apps → Custom. */
export function listCompanionFormChoices(): CompanionFormChoice[] {
  if (cachedCompanionChoices) {
    return cachedCompanionChoices;
  }
  cachedCompanionChoices = [
    { id: COMPANION_PRESET_NONE, title: COMPANION_CHOICE_TITLE_NONE },
    ...listInstalledCompanionPresets(),
    { id: COMPANION_PRESET_CUSTOM, title: COMPANION_CHOICE_TITLE_CUSTOM },
  ];
  return cachedCompanionChoices;
}

export function invalidateCompanionCatalogCache(): void {
  cachedCompanionChoices = null;
}

export function getCompanionPresetDefaultArguments(presetId: string): string {
  if (presetId === COMPANION_PRESET_NONE) {
    return "";
  }
  const preset = COMPANION_PRESETS.find((entry) => entry.id === presetId);
  return preset?.defaultArguments ?? "{folder}";
}

/**
 * Infer a catalog preset from an executable path (exact candidate match, then basename).
 * Returns none / custom when no catalog match.
 */
export function inferCompanionPresetFromPath(executablePath: string | null | undefined): string {
  const trimmed = executablePath?.trim() ?? "";
  if (!trimmed) {
    return COMPANION_PRESET_NONE;
  }

  const normalized = trimmed.replace(/\//g, "\\").toLowerCase();
  for (const preset of COMPANION_PRESETS) {
    for (const candidate of preset.candidatePaths) {
      if (candidate.replace(/\//g, "\\").toLowerCase() === normalized) {
        return preset.id;
      }
    }
  }

  const fileName = basename(trimmed).toLowerCase();
  for (const preset of COMPANION_PRESETS) {
    for (const candidate of preset.candidatePaths) {
      if (basename(candidate).toLowerCase() === fileName) {
        // Explorer basename alone is too ambiguous off Windows\explorer.exe.
        if (preset.id === "explorer" && !normalized.includes("\\windows\\explorer.exe")) {
          continue;
        }
        return preset.id;
      }
    }
  }

  return COMPANION_PRESET_CUSTOM;
}

/** After FilePicker browse: catalog preset if recognized, otherwise Custom. */
export function resolveCompanionPresetAfterBrowse(selectedPath: string): string {
  const inferred = inferCompanionPresetFromPath(selectedPath);
  if (inferred === COMPANION_PRESET_NONE || inferred === COMPANION_PRESET_CUSTOM) {
    return COMPANION_PRESET_CUSTOM;
  }
  return inferred;
}

/**
 * Map a stored path to a dropdown value when the app may no longer be installed.
 */
export function normalizeCompanionPresetForForm(presetId: string | null | undefined, executablePath: string): string {
  const trimmedPath = executablePath.trim();
  const inferred = presetId?.trim() || inferCompanionPresetFromPath(trimmedPath);
  if (inferred === COMPANION_PRESET_NONE || inferred === COMPANION_PRESET_CUSTOM) {
    return trimmedPath ? COMPANION_PRESET_CUSTOM : COMPANION_PRESET_NONE;
  }
  if (resolveCompanionPreset(inferred)) {
    return inferred;
  }
  return trimmedPath ? COMPANION_PRESET_CUSTOM : COMPANION_PRESET_NONE;
}
