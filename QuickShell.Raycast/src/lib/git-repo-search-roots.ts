import path from "node:path";
import os from "node:os";
import { existsSync } from "node:fs";

/** Folder names searched under the user profile and each drive root (parity with Core). */
export const COMMON_ROOT_FOLDER_NAMES = [
  "Projects",
  "projects",
  "dev",
  "Development",
  "code",
  "repos",
  "source",
  "src",
  "Documents",
] as const;

/**
 * Nested paths under the user profile only (not every drive).
 * Includes GitHub Desktop's default: %USERPROFILE%\Documents\GitHub.
 */
export const COMMON_PROFILE_RELATIVE_NESTED_ROOTS = [["Documents", "GitHub"]] as const;

export type BuildSearchRootsOptions = {
  includeDefaultSearchRoots?: boolean;
  /** When set, replaces automatic profile/drive candidate generation (tests / overrides). */
  defaultRootCandidates?: string[];
  home?: string;
  /** Drive roots such as `C:\\`, `D:\\`. When omitted, enumerated on Windows. */
  drives?: string[];
  /** System drive root to exclude from bare-drive candidates (default Windows folder root). */
  systemRoot?: string;
  exists?: (candidate: string) => boolean;
};

/**
 * Workspace-derived roots: each directory plus its parent, skipping parents that are drive roots.
 * Mirrors Core `GitRepoSearchRoots.FromShortcuts`.
 */
export function searchRootsFromWorkspaces(directories: string[]): string[] {
  const roots = new Map<string, string>();

  for (const directory of directories) {
    const trimmed = directory.trim();
    if (!trimmed) {
      continue;
    }

    const normalized = tryNormalizeDirectory(trimmed);
    if (normalized) {
      roots.set(normalized.toLowerCase(), normalized);
    }

    const parent = tryGetParentDirectory(trimmed);
    if (parent) {
      roots.set(parent.toLowerCase(), parent);
    }
  }

  return [...roots.values()];
}

/**
 * Default search roots under the profile and every ready drive.
 * Does **not** include the profile home itself (Core parity).
 */
export function listDefaultRootCandidates(options: BuildSearchRootsOptions = {}): string[] {
  const home = options.home ?? os.homedir();
  const systemRoot = normalizeDriveRoot(
    options.systemRoot ?? path.parse(process.env.SystemRoot || process.env.windir || "C:\\Windows").root,
  );
  const drives = options.drives ?? listWindowsDriveRoots();
  const candidates: string[] = [];

  for (const name of COMMON_ROOT_FOLDER_NAMES) {
    candidates.push(path.join(home, name));
  }

  for (const segments of COMMON_PROFILE_RELATIVE_NESTED_ROOTS) {
    candidates.push(path.join(home, ...segments));
  }

  for (const drive of drives) {
    const root = normalizeDriveRoot(drive);
    for (const name of COMMON_ROOT_FOLDER_NAMES) {
      candidates.push(path.join(root, name));
    }
    if (root.toLowerCase() !== systemRoot.toLowerCase()) {
      candidates.push(root);
    }
  }

  return candidates;
}

/**
 * Build ordered discovery roots: extraRoots first, then defaults.
 * Never adds the user profile home as a scan root.
 */
export function buildSearchRoots(extraRoots: string[] = [], options: BuildSearchRootsOptions = {}): string[] {
  const includeDefaults = options.includeDefaultSearchRoots !== false;
  const exists = options.exists ?? ((candidate: string) => existsSync(candidate));
  const ordered: string[] = [];
  const seen = new Set<string>();

  function add(candidate: string): void {
    const trimmed = candidate.trim();
    if (!trimmed) {
      return;
    }
    const normalized = path.normalize(trimmed);
    const key = normalized.toLowerCase();
    if (seen.has(key)) {
      return;
    }
    if (!exists(normalized)) {
      return;
    }
    seen.add(key);
    ordered.push(normalized);
  }

  for (const root of extraRoots) {
    add(root);
  }

  if (includeDefaults) {
    const defaults = options.defaultRootCandidates ?? listDefaultRootCandidates(options);
    for (const root of defaults) {
      add(root);
    }
  }

  return ordered;
}

function tryNormalizeDirectory(directory: string): string | null {
  try {
    return path.normalize(directory);
  } catch {
    return null;
  }
}

function tryGetParentDirectory(directory: string): string | null {
  try {
    const trimmed = directory.trim().replace(/[\\/]+$/, "");
    const parent = path.dirname(trimmed);
    if (!parent || parent === trimmed) {
      return null;
    }

    const driveRoot = path.parse(parent).root;
    if (driveRoot && parent.toLowerCase() === driveRoot.toLowerCase()) {
      return null;
    }

    return path.normalize(parent);
  } catch {
    return null;
  }
}

function normalizeDriveRoot(drive: string): string {
  const normalized = path.normalize(drive.trim());
  if (/^[a-zA-Z]:\\?$/.test(normalized)) {
    return `${normalized.replace(/\\$/, "")}\\`;
  }
  return normalized.endsWith(path.sep) ? normalized : `${normalized}${path.sep}`;
}

function listWindowsDriveRoots(): string[] {
  if (process.platform !== "win32") {
    return [];
  }

  const roots: string[] = [];
  for (let code = "A".charCodeAt(0); code <= "Z".charCodeAt(0); code += 1) {
    const letter = String.fromCharCode(code);
    const root = `${letter}:\\`;
    try {
      if (existsSync(root)) {
        roots.push(root);
      }
    } catch {
      // Ignore inaccessible drive letters.
    }
  }
  return roots;
}
