import { existsSync, readdirSync, statSync } from "node:fs";
import path from "node:path";
import os from "node:os";

export type GitRepoCandidate = {
  directory: string;
  name: string;
  remoteUrl?: string | null;
};

const SKIP_DIRS = new Set([
  ".git",
  "node_modules",
  "bin",
  "obj",
  "dist",
  "build",
  "out",
  "target",
  "AppData",
  "Program Files",
  "Program Files (x86)",
  "Windows",
  ".nuget",
  ".vscode",
  ".cursor",
]);

const COMMON_ROOTS = ["Projects", "projects", "dev", "Development", "code", "repos", "source", "src"];

const MAX_REPOS = 50;
const MAX_SCANNED = 2000;
const MAX_DEPTH = 5;

export function discoverGitRepos(extraRoots: string[] = []): GitRepoCandidate[] {
  if (process.platform !== "win32") {
    return [];
  }

  const roots = buildSearchRoots(extraRoots);
  const results: GitRepoCandidate[] = [];
  const seen = new Set<string>();
  let scanned = 0;

  for (const root of roots) {
    scanDirectory(root, 0);
    if (results.length >= MAX_REPOS || scanned >= MAX_SCANNED) {
      break;
    }
  }

  return results.sort((left, right) => left.name.localeCompare(right.name, undefined, { sensitivity: "base" }));

  function scanDirectory(directory: string, depth: number): void {
    if (results.length >= MAX_REPOS || scanned >= MAX_SCANNED || depth > MAX_DEPTH) {
      return;
    }
    if (!existsSync(directory)) {
      return;
    }

    scanned += 1;
    const gitDir = path.join(directory, ".git");
    if (existsSync(gitDir)) {
      const normalized = path.normalize(directory);
      if (!seen.has(normalized.toLowerCase())) {
        seen.add(normalized.toLowerCase());
        results.push({
          directory: normalized,
          name: path.basename(normalized),
          remoteUrl: readRemoteUrl(normalized),
        });
      }
      return;
    }

    let entries: string[] = [];
    try {
      entries = readdirSync(directory);
    } catch {
      return;
    }

    for (const entry of entries) {
      if (SKIP_DIRS.has(entry)) {
        continue;
      }
      const fullPath = path.join(directory, entry);
      let isDirectory = false;
      try {
        isDirectory = statSync(fullPath).isDirectory();
      } catch {
        continue;
      }
      if (isDirectory) {
        scanDirectory(fullPath, depth + 1);
      }
      if (results.length >= MAX_REPOS || scanned >= MAX_SCANNED) {
        return;
      }
    }
  }
}

export function buildSearchRoots(extraRoots: string[] = []): string[] {
  const roots = new Set<string>();
  const home = os.homedir();

  for (const name of COMMON_ROOTS) {
    roots.add(path.join(home, name));
  }
  roots.add(path.join(home, "Documents"));
  roots.add(home);

  for (const root of extraRoots) {
    if (root.trim()) {
      roots.add(path.normalize(root.trim()));
    }
  }

  return [...roots].filter((root) => existsSync(root));
}

function readRemoteUrl(directory: string): string | null {
  const headPath = path.join(directory, ".git", "HEAD");
  if (!existsSync(headPath)) {
    return null;
  }
  return null;
}
