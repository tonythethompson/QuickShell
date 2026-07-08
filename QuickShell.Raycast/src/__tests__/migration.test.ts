import { describe, expect, it } from "vitest";
import { migrateStoredData, normalizeRecentCount } from "../lib/migration";

describe("migration", () => {
  it("migrates legacy entries field to launches", () => {
    const data = migrateStoredData({
      version: 0,
      workspaces: [
        {
          id: "a1b2c3d4e5f6478990a1b2c3d4e5f678",
          name: "Demo",
          directory: "C:\\Projects\\Demo",
          entries: [
            {
              id: "b2c3d4e5f6478990a1b2c3d4e5f67890",
              label: "API",
              terminal: "wt",
              command: "dotnet run",
              isEnabled: true,
              order: 0,
            },
          ],
        },
      ],
    });

    expect(data.version).toBe(1);
    expect(data.workspaces[0].launches).toHaveLength(1);
    expect(data.workspaces[0].launches[0].label).toBe("API");
  });

  it("normalizes recent count to 0 or 8", () => {
    expect(normalizeRecentCount(0)).toBe(0);
    expect(normalizeRecentCount(3)).toBe(8);
    expect(normalizeRecentCount(12)).toBe(8);
  });

  it("defaults multiLaunchPresentation to singleWindowTabs", () => {
    const data = migrateStoredData({
      version: 1,
      settings: {
        terminalApplication: "wt",
        defaultProfile: "__default__",
        recentWorkspaceCount: 8,
      },
      workspaces: [],
    });

    expect(data.settings.multiLaunchPresentation).toBe("singleWindowTabs");
  });

  it("preserves separateWindows multiLaunchPresentation", () => {
    const data = migrateStoredData({
      version: 1,
      settings: {
        terminalApplication: "wt",
        defaultProfile: "__default__",
        recentWorkspaceCount: 8,
        multiLaunchPresentation: "separateWindows",
      },
      workspaces: [],
    });

    expect(data.settings.multiLaunchPresentation).toBe("separateWindows");
  });

  it("does not treat string false as true for workspace flags", () => {
    const data = migrateStoredData({
      version: 1,
      settings: {
        terminalApplication: "wt",
        defaultProfile: "__default__",
        recentWorkspaceCount: 8,
      },
      workspaces: [
        {
          id: "a1b2c3d4e5f6478990a1b2c3d4e5f678",
          name: "Demo",
          directory: "C:\\Projects\\Demo",
          openDevServerOnLaunch: "false",
          openCompanionAppOnLaunch: "false",
          launches: [],
        },
      ],
    });

    expect(data.workspaces[0].openDevServerOnLaunch).toBe(false);
    expect(data.workspaces[0].openCompanionAppOnLaunch).toBe(false);
  });
});
