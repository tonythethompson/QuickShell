import { describe, expect, it } from "vitest";
import {
  buildBrowseSections,
  computeBrowseScore,
  getRecentWorkspaces,
  sortWorkspacesForSearch,
} from "../lib/ranking";
import type { Workspace } from "../lib/schema";

function workspace(
  partial: Partial<Workspace> & Pick<Workspace, "id" | "name">,
): Workspace {
  return {
    id: partial.id,
    name: partial.name,
    abbreviation: partial.abbreviation ?? null,
    directory: partial.directory ?? `C:\\Projects\\${partial.name}`,
    isPinned: partial.isPinned ?? false,
    pinOrder: partial.pinOrder ?? null,
    lastUsedUtc: partial.lastUsedUtc ?? null,
    terminal: "default",
    wtProfile: null,
    command: null,
    runAsAdmin: false,
    launches: partial.launches ?? [
      {
        id: `${partial.id}launch`,
        label: "Launch",
        terminal: "default",
        wtProfile: null,
        command: null,
        runAsAdmin: false,
        isEnabled: true,
        order: 0,
        taskType: "none",
      },
    ],
  };
}

describe("ranking", () => {
  it("ranks pinned workspaces above recents in browse mode", () => {
    const favorite = workspace({
      id: "1",
      name: "Favorite",
      isPinned: true,
      pinOrder: 1,
    });
    const recent = workspace({
      id: "2",
      name: "Recent",
      lastUsedUtc: new Date().toISOString(),
    });

    expect(computeBrowseScore(favorite)).toBeGreaterThan(
      computeBrowseScore(recent),
    );
  });

  it("returns favorites, recents, and remaining workspaces", () => {
    const favorite = workspace({
      id: "1",
      name: "Favorite",
      isPinned: true,
      pinOrder: 1,
    });
    const recent = workspace({
      id: "2",
      name: "Recent",
      lastUsedUtc: "2026-07-06T12:00:00.000Z",
    });
    const other = workspace({ id: "3", name: "Other" });

    const sections = buildBrowseSections([favorite, recent, other], 8);
    expect(sections.favorites.map((item) => item.id)).toEqual(["1"]);
    expect(sections.recents.map((item) => item.id)).toEqual(["2"]);
    expect(sections.workspaces.map((item) => item.id)).toEqual(["3"]);
  });

  it("limits recents to eight non-pinned workspaces", () => {
    const recents = Array.from({ length: 10 }, (_, index) =>
      workspace({
        id: String(index),
        name: `Recent ${index}`,
        lastUsedUtc: new Date(2026, 6, index + 1).toISOString(),
      }),
    );

    expect(getRecentWorkspaces(recents, 8)).toHaveLength(8);
  });

  it("ranks exact abbreviation matches higher in search mode", () => {
    const exact = workspace({ id: "1", name: "Alpha", abbreviation: "api" });
    const other = workspace({
      id: "2",
      name: "Beta",
      abbreviation: "app",
      isPinned: true,
    });

    const ranked = sortWorkspacesForSearch([exact, other], "api");
    expect(ranked[0].id).toBe("1");
  });
});
