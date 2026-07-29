import { describe, expect, it } from "vitest";
import { createWorkspaceFromDiscoveredGitRepo } from "../lib/discovered-workspace-seed";

describe("createWorkspaceFromDiscoveredGitRepo", () => {
  it("preserves commands, companion app, and metadata for a newly discovered repository", () => {
    const workspace = createWorkspaceFromDiscoveredGitRepo({
      directory: "D:\\Dev\\Trackdub_Workspace\\Trackdub",
      name: "Trackdub",
      remoteUrl: "https://github.com/trackdub/trackdub",
      devServerUrl: "http://localhost:5173",
      tasks: [
        { label: "Dev server", command: "npm run dev", taskType: "frontend" },
        { label: "API", command: "dotnet watch", taskType: "api" },
      ],
      companionSeed: { path: "C:\\Apps\\Cursor.exe", arguments: "." },
    });

    expect(workspace.name).toBe("Trackdub");
    expect(workspace.abbreviation).toBe("trackdub");
    expect(workspace.repoUrl).toBe("https://github.com/trackdub/trackdub");
    expect(workspace.devServerUrl).toBe("http://localhost:5173");
    expect(workspace.launches.map((launch) => launch.command)).toEqual(["npm run dev", "dotnet watch"]);
    expect(workspace.launches.map((launch) => launch.taskType)).toEqual(["frontend", "api"]);
    expect(workspace.companionApps).toEqual([
      expect.objectContaining({ path: "C:\\Apps\\Cursor.exe", arguments: ".", openOnLaunch: true }),
    ]);
  });

  it("keeps a usable blank launch row when no command suggestion is available", () => {
    const workspace = createWorkspaceFromDiscoveredGitRepo({
      directory: "D:\\Dev\\empty-repo",
      name: "",
      tasks: [],
    });

    expect(workspace.name).toBe("empty-repo");
    expect(workspace.launches).toHaveLength(1);
    expect(workspace.launches[0].command).toBeNull();
  });
});
