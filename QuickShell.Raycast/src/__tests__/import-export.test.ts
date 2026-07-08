import { describe, expect, it } from "vitest";
import { importParsedPayload } from "../lib/import-export";
import { createEmptyStoredData } from "../lib/schema";
import { createStableId } from "../lib/ids";
import { normalizeWorkspace } from "../lib/validation";

describe("import-export", () => {
  it("imports CmdPal shortcut arrays with pascal case keys", () => {
    const result = importParsedPayload([
      {
        Name: "Frontend",
        Abbreviation: "fe",
        Directory: "C:\\Projects\\web",
        Command: "npm run dev",
        Terminal: "wt",
        DevServerUrl: "http://localhost:5173",
        OpenDevServerOnLaunch: true,
      },
    ]);

    expect(result.imported).toBe(1);
    expect(result.data.workspaces[0].name).toBe("Frontend");
    expect(result.data.workspaces[0].devServerUrl).toBe("http://localhost:5173");
    expect(result.data.workspaces[0].openDevServerOnLaunch).toBe(true);
  });

  it("merges without duplicating names", () => {
    const existing = createEmptyStoredData();
    existing.workspaces.push(
      normalizeWorkspace({
        id: createStableId(),
        name: "Frontend",
        abbreviation: "fe",
        directory: "C:\\Projects\\web",
        isPinned: false,
        pinOrder: null,
        lastUsedUtc: null,
        terminal: "wt",
        wtProfile: null,
        command: null,
        runAsAdmin: false,
        launches: [
          {
            id: createStableId(),
            label: "Launch",
            terminal: "wt",
            wtProfile: null,
            command: null,
            runAsAdmin: false,
            isEnabled: true,
            order: 0,
            taskType: "none",
          },
        ],
      }),
    );

    const result = importParsedPayload(
      [{ name: "Frontend", directory: "C:\\Projects\\web-2", command: "npm run dev", terminal: "wt" }],
      existing,
    );

    expect(result.imported).toBe(1);
    expect(result.renamed).toBe(1);
    expect(result.data.workspaces.some((workspace) => workspace.name.includes("imported"))).toBe(true);
  });
});
