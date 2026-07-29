import { existsSync, readdirSync, statSync, promises as fs } from "node:fs";
import path from "node:path";
import { withCache } from "@raycast/utils";
import { isMacPlatform, isWindowsPlatform } from "./platform";
import { buildSearchRoots, searchRootsFromWorkspaces } from "./git-repo-search-roots";

export { tryGetGitRemoteUrl } from "./git-remote-url";
export {
  buildSearchRoots,
  listDefaultRootCandidates,
  searchRootsFromWorkspaces,
  COMMON_ROOT_FOLDER_NAMES,
} from "./git-repo-search-roots";

export type GitRepoCandidate = {
  directory: string;
  name: string;
  remoteUrl?: string | null;
};

const SKIP_DIRS = new Set(
  [
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
  ].map((name) => name.toLowerCase()),
);

const MAX_REPOS = 50;
const MAX_SCANNED = 2000;
const MAX_TARGETED_SCANNED = 20000;
const MAX_DEPTH = 5;
const DEFAULT_CONCURRENCY = 4;

type DiscoveryOptions = {
  concurrency?: number;
  maxRepos?: number;
  maxScanned?: number;
  query?: string;
  rootDirectories?: string[];
};

export function discoverGitRepos(extraRoots: string[] = []): GitRepoCandidate[] {
  return discoverGitReposSync(extraRoots);
}

export async function discoverGitReposCached(extraRoots: string[] = []): Promise<GitRepoCandidate[]> {
  return cachedDiscoverGitRepos(extraRoots);
}

const cachedDiscoverGitRepos = withCache(async (extraRoots: string[] = []) => discoverGitReposAsync(extraRoots), {
  maxAge: 10 * 60 * 1000,
});

export async function discoverGitReposAsync(
  extraRoots: string[] = [],
  options?: DiscoveryOptions,
): Promise<GitRepoCandidate[]> {
  if (!isWindowsPlatform() && !isMacPlatform()) {
    return [];
  }

  const roots = options?.rootDirectories ?? buildSearchRoots(extraRoots);
  if (roots.length === 0) {
    return [];
  }

  const results: GitRepoCandidate[] = [];
  const seen = new Set<string>();
  let scanned = 0;
  const queue: Array<{ directory: string; depth: number }> = roots.map((directory) => ({
    directory,
    depth: 0,
  }));
  const concurrency = Math.max(1, options?.concurrency ?? DEFAULT_CONCURRENCY);
  const maxRepos = options?.maxRepos ?? MAX_REPOS;
  const maxScanned = options?.maxScanned ?? MAX_SCANNED;
  const query = normalizeDiscoveryQuery(options?.query ?? "");

  async function worker(): Promise<void> {
    while (results.length < maxRepos && scanned < maxScanned) {
      const work = queue.shift();
      if (!work) {
        return;
      }
      if (work.depth > MAX_DEPTH) {
        continue;
      }

      let isDirectory = false;
      try {
        isDirectory = (await fs.stat(work.directory)).isDirectory();
      } catch {
        continue;
      }
      if (!isDirectory) {
        continue;
      }

      if (existsSync(path.join(work.directory, ".git"))) {
        addRepo(work.directory, results, seen, query, maxRepos);
        continue;
      }

      // Match Core: only non-repo directories consume the scan budget.
      scanned += 1;
      if (scanned >= maxScanned || results.length >= maxRepos) {
        return;
      }

      let entries: Array<{ name: string; isDirectory: () => boolean }> = [];
      try {
        entries = await fs.readdir(work.directory, { withFileTypes: true });
      } catch {
        continue;
      }

      for (const entry of entries) {
        if (!entry.isDirectory() || SKIP_DIRS.has(entry.name.toLowerCase())) {
          continue;
        }
        queue.push({
          directory: path.join(work.directory, entry.name),
          depth: work.depth + 1,
        });
      }
    }
  }

  while (queue.length > 0 && results.length < maxRepos && scanned < maxScanned) {
    const waveSize = Math.min(concurrency, queue.length);
    await Promise.all(Array.from({ length: waveSize }, () => worker()));
  }

  return sortCandidates(results);
}

/**
 * Search beyond the normal 50-result discovery cap. An exact path bypasses the
 * tree scan entirely, while a name query filters during a larger targeted scan.
 */
export async function discoverGitReposForQueryAsync(
  searchText: string,
  extraRoots: string[] = [],
  options?: { rootDirectories?: string[]; concurrency?: number; maxScanned?: number },
): Promise<GitRepoCandidate[]> {
  const query = normalizeDiscoveryQuery(searchText);
  if (!query) {
    return [];
  }

  const pathMatch = repoCandidateFromPathQuery(searchText);
  if (pathMatch) {
    return [pathMatch];
  }

  return discoverGitReposAsync(extraRoots, {
    concurrency: options?.concurrency,
    maxRepos: Number.POSITIVE_INFINITY,
    maxScanned: options?.maxScanned ?? MAX_TARGETED_SCANNED,
    query,
    rootDirectories: options?.rootDirectories,
  });
}

function discoverGitReposSync(extraRoots: string[] = []): GitRepoCandidate[] {
  if (!isWindowsPlatform() && !isMacPlatform()) {
    return [];
  }

  const roots = buildSearchRoots(extraRoots);
  const results: GitRepoCandidate[] = [];
  const seen = new Set<string>();
  let scanned = 0;
  const queue: Array<{ directory: string; depth: number }> = roots.map((directory) => ({
    directory,
    depth: 0,
  }));

  while (queue.length > 0 && results.length < MAX_REPOS && scanned < MAX_SCANNED) {
    const work = queue.shift();
    if (!work || work.depth > MAX_DEPTH) {
      continue;
    }

    let isDirectory = false;
    try {
      isDirectory = statSync(work.directory).isDirectory();
    } catch {
      continue;
    }
    if (!isDirectory) {
      continue;
    }

    if (existsSync(path.join(work.directory, ".git"))) {
      addRepo(work.directory, results, seen);
      continue;
    }

    scanned += 1;
    if (scanned >= MAX_SCANNED) {
      break;
    }

    let entries: Array<{ name: string; isDirectory: () => boolean }> = [];
    try {
      entries = readdirSync(work.directory, { withFileTypes: true });
    } catch {
      continue;
    }

    for (const entry of entries) {
      if (!entry.isDirectory() || SKIP_DIRS.has(entry.name.toLowerCase())) {
        continue;
      }
      queue.push({
        directory: path.join(work.directory, entry.name),
        depth: work.depth + 1,
      });
    }
  }

  return sortCandidates(results);
}

function addRepo(
  directory: string,
  results: GitRepoCandidate[],
  seen: Set<string>,
  query = "",
  maxRepos = MAX_REPOS,
): void {
  if (results.length >= maxRepos) {
    return;
  }
  const normalized = path.normalize(directory);
  const key = normalized.toLowerCase();
  if (seen.has(key)) {
    return;
  }
  seen.add(key);
  const name = path.basename(normalized);
  if (query && !name.toLowerCase().includes(query) && !key.includes(query)) {
    return;
  }
  results.push({
    directory: normalized,
    name,
    remoteUrl: null,
  });
}

function normalizeDiscoveryQuery(value: string): string {
  return value
    .trim()
    .replace(/^['"]|['"]$/g, "")
    .toLowerCase();
}

function repoCandidateFromPathQuery(value: string): GitRepoCandidate | null {
  let candidate = value.trim().replace(/^['"]|['"]$/g, "");
  if (!candidate || !path.isAbsolute(candidate)) {
    return null;
  }

  try {
    if (!statSync(candidate).isDirectory()) {
      candidate = path.dirname(candidate);
    }
  } catch {
    return null;
  }

  while (true) {
    if (existsSync(path.join(candidate, ".git"))) {
      const directory = path.normalize(candidate);
      return { directory, name: path.basename(directory), remoteUrl: null };
    }
    const parent = path.dirname(candidate);
    if (parent === candidate) {
      return null;
    }
    candidate = parent;
  }
}

function sortCandidates(results: GitRepoCandidate[]): GitRepoCandidate[] {
  return results.sort((left, right) => left.name.localeCompare(right.name, undefined, { sensitivity: "base" }));
}

/** Build extra roots from saved workspace directories (dir + parent). */
export function extraRootsFromWorkspaceDirectories(directories: string[]): string[] {
  return searchRootsFromWorkspaces(directories);
}
