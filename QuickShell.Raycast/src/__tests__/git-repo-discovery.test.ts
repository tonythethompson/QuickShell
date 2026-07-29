import { describe, expect, it, vi } from "vitest";
import { mkdirSync, mkdtempSync, rmSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import {
  buildSearchRoots,
  discoverGitReposForQueryAsync,
  listDefaultRootCandidates,
  searchRootsFromWorkspaces,
} from "../lib/git-repo-discovery";

vi.mock("@raycast/utils", () => ({
  withCache: <T extends (...args: never[]) => unknown>(fn: T) => fn,
}));

/** Windows-path contracts must not depend on the host OS (macOS CI uses darwin). */
const win32Roots = { pathStyle: "win32" as const };

describe("searchRootsFromWorkspaces", () => {
  it("includes each workspace directory and its parent, skipping drive-root parents", () => {
    const roots = searchRootsFromWorkspaces(
      ["D:\\Dev\\QuickShell", "D:\\Dev\\Trackdub", "C:\\Users\\tonyt\\source\\repos\\demo"],
      win32Roots,
    );

    expect(roots.map((root) => root.toLowerCase())).toEqual(
      expect.arrayContaining([
        "d:\\dev\\quickshell",
        "d:\\dev",
        "d:\\dev\\trackdub",
        "c:\\users\\tonyt\\source\\repos\\demo",
        "c:\\users\\tonyt\\source\\repos",
      ]),
    );
    expect(roots.map((root) => root.toLowerCase())).not.toContain("d:\\");
  });

  it("dedupes case-insensitively", () => {
    const roots = searchRootsFromWorkspaces(["D:\\Dev\\QuickShell", "d:\\dev\\QuickShell"], win32Roots);
    const lower = roots.map((root) => root.toLowerCase());
    expect(new Set(lower).size).toBe(lower.length);
  });
});

describe("listDefaultRootCandidates", () => {
  it("adds common folders under profile and every drive, plus non-system drive roots", () => {
    const candidates = listDefaultRootCandidates({
      ...win32Roots,
      home: "C:\\Users\\tonyt",
      drives: ["C:\\", "D:\\"],
      systemRoot: "C:\\",
    });

    const lower = candidates.map((candidate) => candidate.toLowerCase());
    expect(lower).toEqual(
      expect.arrayContaining([
        "c:\\users\\tonyt\\projects",
        "c:\\users\\tonyt\\development",
        "c:\\users\\tonyt\\documents",
        "c:\\users\\tonyt\\documents\\github",
        "c:\\projects",
        "c:\\dev",
        "d:\\dev",
        "d:\\projects",
        "d:\\",
      ]),
    );
    expect(lower).not.toContain("c:\\users\\tonyt");
    expect(lower).not.toContain("c:\\");
  });

  it("resolves profile nested roots from the real username home, not a literal YourName", () => {
    const candidates = listDefaultRootCandidates({
      ...win32Roots,
      home: "C:\\Users\\actual.user",
      drives: ["C:\\"],
      systemRoot: "C:\\",
    });

    expect(candidates.map((candidate) => candidate.toLowerCase())).toContain(
      "c:\\users\\actual.user\\documents\\github",
    );
    expect(candidates.join("\n").toLowerCase()).not.toContain("yourname");
  });
});

describe("buildSearchRoots", () => {
  it("puts extra roots first, includes defaults, and never adds the home profile root", () => {
    const exists = new Set(["d:\\dev", "c:\\users\\tonyt\\projects", "c:\\users\\tonyt", "d:\\"]);

    const roots = buildSearchRoots(["D:\\Dev"], {
      ...win32Roots,
      home: "C:\\Users\\tonyt",
      defaultRootCandidates: listDefaultRootCandidates({
        ...win32Roots,
        home: "C:\\Users\\tonyt",
        drives: ["C:\\", "D:\\"],
        systemRoot: "C:\\",
      }),
      exists: (candidate) => exists.has(candidate.toLowerCase()),
    });

    expect(roots[0].toLowerCase()).toBe("d:\\dev");
    expect(roots.map((root) => root.toLowerCase())).toContain("d:\\");
    expect(roots.map((root) => root.toLowerCase())).toContain("c:\\users\\tonyt\\projects");
    expect(roots.map((root) => root.toLowerCase())).not.toContain("c:\\users\\tonyt");
  });
});

describe("discoverGitReposForQueryAsync", () => {
  it("bypasses the normal result cap for typed name searches", async () => {
    const root = mkdtempSync(path.join(os.tmpdir(), "quickshell-git-search-"));
    try {
      for (let index = 0; index < 55; index += 1) {
        mkdirSync(path.join(root, `repo-${index.toString().padStart(2, "0")}`, ".git"), { recursive: true });
      }

      const repos = await discoverGitReposForQueryAsync("repo-", [], { rootDirectories: [root], concurrency: 1 });

      expect(repos).toHaveLength(55);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });

  it("finds the containing repository directly from an absolute path", async () => {
    const root = mkdtempSync(path.join(os.tmpdir(), "quickshell-git-path-"));
    const repo = path.join(root, "Trackdub");
    const nested = path.join(repo, "src", "features");
    try {
      mkdirSync(path.join(repo, ".git"), { recursive: true });
      mkdirSync(nested, { recursive: true });

      const repos = await discoverGitReposForQueryAsync(nested);

      expect(repos).toEqual([{ directory: repo, name: "Trackdub", remoteUrl: null }]);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });

  it("stops a targeted scan when it is already cancelled", async () => {
    const controller = new AbortController();
    controller.abort();

    const repos = await discoverGitReposForQueryAsync("repo", [], { signal: controller.signal });

    expect(repos).toEqual([]);
  });

  it("bounds total visited repositories without restoring the result cap", async () => {
    const root = mkdtempSync(path.join(os.tmpdir(), "quickshell-git-budget-"));
    try {
      for (let index = 0; index < 20; index += 1) {
        mkdirSync(path.join(root, `repo-${index.toString().padStart(2, "0")}`, ".git"), { recursive: true });
      }

      const repos = await discoverGitReposForQueryAsync("repo-", [], {
        rootDirectories: [root],
        concurrency: 1,
        maxVisited: 11,
      });

      expect(repos).toHaveLength(10);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });
});
