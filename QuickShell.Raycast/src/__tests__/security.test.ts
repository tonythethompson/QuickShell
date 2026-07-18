import { describe, expect, it } from "vitest";
import { authorize, createReviewToken, matchesReviewToken } from "../lib/security";
import type { StoredWorkspace, Workspace } from "../lib/schema";

const workspace: Workspace = {
  id: "workspace-1",
  name: "Workspace",
  directory: "C:\\Projects\\Workspace",
  terminal: "wt",
  command: "powershell -NoProfile",
  runAsAdmin: true,
  launches: [
    {
      id: "launch-1",
      label: "Launch",
      terminal: "wt",
      command: "echo hello",
      runAsAdmin: true,
      isEnabled: true,
      order: 0,
    },
  ],
  isPinned: false,
};

function stored(isTrusted: boolean): StoredWorkspace {
  return { content: workspace, security: { isTrusted, revision: 3 }, revision: 3 };
}

describe("workspace security policy", () => {
  it("blocks untrusted launch and directory actions while allowing copy path", () => {
    expect(authorize(stored(false), "LaunchTerminal").isAllowed).toBe(false);
    expect(authorize(stored(false), "OpenDirectory").isAllowed).toBe(false);
    expect(authorize(stored(false), "CopyPath").isAllowed).toBe(true);
  });

  it("returns risk findings for command and elevation content", () => {
    const result = authorize(stored(true), "GrantTrust");
    expect(result.risks.map((risk) => risk.code)).toEqual(expect.arrayContaining(["command", "elevation"]));
  });

  it("binds review to the authoritative revision and content digest", () => {
    const review = createReviewToken(stored(false));
    expect(matchesReviewToken(stored(false), review)).toBe(true);
    expect(matchesReviewToken({ ...stored(false), revision: 4 }, review)).toBe(false);
  });

  it("rejects unsafe URL schemes and control characters", () => {
    const malformed = { ...workspace, repoUrl: "javascript:alert(1)" };
    const result = authorize({ ...stored(true), content: malformed }, "OpenUrl");
    expect(result.isAllowed).toBe(false);
    expect(result.primaryIssueCode).toBe("InvalidUrl");
  });
});
